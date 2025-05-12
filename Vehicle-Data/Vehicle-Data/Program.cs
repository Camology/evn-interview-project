using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Vehicle_Data;
using Vehicle_Data.Models;

// Ensure the database is created
using var vehicle_db = new VehicleContext();
using var error_db = new ErrorVehicleContext();

try {
    // This will create the database if it doesn't exist
    vehicle_db.Database.EnsureCreated();
    error_db.Database.EnsureCreated();
    InitializeDb.Initialize(vehicle_db, error_db).Wait();
    Console.WriteLine("Databases initialized.");
}
catch (Exception ex){
    Console.WriteLine($"Error initializing databases: {ex.Message}");
}

// Web application setup
var builder = WebApplication.CreateBuilder(args);

// Explicitly set the URLs for the application
builder.WebHost.UseUrls("https://localhost:5001", "http://localhost:5000");

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddDbContext<VehicleContext>();
builder.Services.AddDbContext<ErrorVehicleContext>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
else
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
}

app.UseHttpsRedirection();
app.UseWebSockets();
app.UseStaticFiles();
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
