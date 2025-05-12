using Vehicle_Data.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Vehicle_Data {
    public static class InitializeDb {
        public static async Task Initialize(AppDbContext db) {
            Console.WriteLine($"Database path: {db.DbPath}.");
            Console.WriteLine("Checking for existing data...");

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
                    if (!db.Vehicles.Any(v => v.Vin == vehicleCsv.vin)) {
                        db.Vehicles.Add(new Vehicle {
                            DealerId = vehicleCsv.dealerId,
                            Vin = vehicleCsv.vin,
                            ModifiedDate = DateOnly.Parse(vehicleCsv.modifiedDate),
                            Make = null,
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

            await UpdateVehicleDataFromApi(db);
        }

        private static async Task UpdateVehicleDataFromApi(AppDbContext db) {
            Console.WriteLine("Updating vehicle data from NHTSA API...");
            using var httpClient = new HttpClient();

            var vehicles = db.Vehicles.ToList();
            foreach (var vehicle in vehicles) {
                var apiUrl = $"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVin/{vehicle.Vin}?format=json";
                Console.WriteLine($"Calling API for VIN: {vehicle.Vin}");

                try {
                    var response = await httpClient.GetStringAsync(apiUrl);
                    var json = JObject.Parse(response);
                    var results = json["Results"];

                    var errorCode = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Error Code")?["Value"]?.ToString();
                    var errorText = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Error Text")?["Value"]?.ToString();

                    if (!string.IsNullOrEmpty(errorCode) && errorCode != "0" && errorText != "1 - Check Digit (9th position) does not calculate properly") {
                        Console.WriteLine($"Error for VIN {vehicle.Vin}: Code={errorCode}, Text={errorText}");

                        db.Vehicles.Remove(vehicle);

                        db.ErrorVehicles.Add(new ErrorVehicle {
                            Vin = vehicle.Vin,
                            DealerId = vehicle.DealerId,
                            ModifiedDate = vehicle.ModifiedDate,
                            ErrorCode = errorCode,
                            ErrorText = errorText
                        });

                        continue;
                    }

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