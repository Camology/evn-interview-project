using Vehicle_Data.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Vehicle_Data {
    public static class InitializeDb {
        public static async Task Initialize(VehicleContext db) {
            Console.WriteLine($"Database path: {db.DbPath}.");
            Console.WriteLine("Checking for existing data...");

            // Adjust the path to locate the initial_data folder relative to the project directory
            var projectDirectory = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.Parent?.Parent?.FullName;
            var csvFilePath = Path.Combine(projectDirectory ?? string.Empty, "Data", "sample-vin-data.csv");

            Console.WriteLine($"Looking for CSV file at: {csvFilePath}");

            if (File.Exists(csvFilePath)) {
                Console.WriteLine("Loading data from CSV...");
                using var reader = new StreamReader(csvFilePath);
                using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                });

                var vehicles = csv.GetRecords<VehicleCsv>().ToList();

                foreach (var vehicleCsv in vehicles) {
                    // Check if the vehicle already exists in the database
                    if (!db.Vehicles.Any(v => v.Vin == vehicleCsv.vin)) {
                        db.Vehicles.Add(new Vehicle {
                            DealerId = vehicleCsv.dealerId,
                            Vin = vehicleCsv.vin,
                            ModifiedDate = DateOnly.Parse(vehicleCsv.modifiedDate),
                            Make = null, // Set default values for optional fields
                            Model = null,
                            Year = null,
                        });
                    }
                }

                await db.SaveChangesAsync();
                Console.WriteLine("CSV data loaded into the database.");
            }
            else {
                Console.WriteLine("CSV file not found. Skipping data load.");
            }

            // Update database rows with data from the NHTSA API
            await UpdateVehicleDataFromApi(db);
        }

        private static async Task UpdateVehicleDataFromApi(VehicleContext db) {
            Console.WriteLine("Updating vehicle data from NHTSA API...");
            using var httpClient = new HttpClient();

            var vehicles = db.Vehicles.ToList();
            foreach (var vehicle in vehicles) {
                var apiUrl = $"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVin/{vehicle.Vin}?format=json";
                Console.WriteLine($"Calling API for VIN: {vehicle.Vin}");

                try {
                    var response = await httpClient.GetStringAsync(apiUrl);
                    var json = JObject.Parse(response);

                    // Extract "Make", "Model", and "Year" from the API response
                    var results = json["Results"];
                    vehicle.Make = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Make")?["Value"]?.ToString();
                    vehicle.Model = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Model")?["Value"]?.ToString();
                    var yearString = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Model Year")?["Value"]?.ToString();
                    vehicle.Year = int.TryParse(yearString, out var year) ? year : (int?)null;

                    Console.WriteLine($"Updated Vehicle: VIN={vehicle.Vin}, Make={vehicle.Make}, Model={vehicle.Model}, Year={vehicle.Year}");
                }
                catch (Exception ex) {
                    Console.WriteLine($"Failed to update vehicle with VIN {vehicle.Vin}: {ex.Message}");
                }
            }

            await db.SaveChangesAsync();
            Console.WriteLine("Vehicle data updated from NHTSA API.");
        }
    }
}