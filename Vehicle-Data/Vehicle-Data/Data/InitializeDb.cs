using Vehicle_Data.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

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
                            Color = null
                        });
                    }
                }

                await db.SaveChangesAsync();
                Console.WriteLine("CSV data loaded into the database.");
            }
            else {
                Console.WriteLine("CSV file not found. Skipping data load.");
            }
        }
    }
}