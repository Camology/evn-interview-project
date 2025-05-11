using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Vehicle_Data.Models;

// Ensure the database is created
using var db = new VehicleContext();
db.Database.EnsureCreated(); // This ensures the database and schema are created if they don't exist

Console.WriteLine($"Database path: {db.DbPath}.");
Console.WriteLine("Checking for existing data...");

// Adjust the path to locate the initial_data folder relative to the project directory
var projectDirectory = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.Parent?.Parent?.FullName;
var csvFilePath = Path.Combine(projectDirectory ?? string.Empty, "initial_data", "sample-vin-data.csv");

Console.WriteLine($"Looking for CSV file at: {csvFilePath}");

if (File.Exists(csvFilePath))
{
    Console.WriteLine("Loading data from CSV...");
    using var reader = new StreamReader(csvFilePath);
    using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
    });

    var vehicles = csv.GetRecords<VehicleCsv>().ToList();

    foreach (var vehicleCsv in vehicles)
    {
        // Check if the vehicle already exists in the database
        if (!db.Vehicles.Any(v => v.Vin == vehicleCsv.vin))
        {
            db.Vehicles.Add(new Vehicle
            {
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
else
{
    Console.WriteLine("CSV file not found. Skipping data load.");
}

// Web application setup
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// CSV mapping class
public class VehicleCsv
{
    public int dealerId { get; set; }
    public required string vin { get; set; }
    public required string  modifiedDate { get; set; }
}
