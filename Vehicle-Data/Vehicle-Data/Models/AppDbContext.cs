namespace Vehicle_Data.Models;

using Microsoft.EntityFrameworkCore;
using System;

public class AppDbContext : DbContext
{
    public DbSet<Vehicle> Vehicles { get; set; }
    public DbSet<ErrorVehicle> ErrorVehicles { get; set; }

    public string DbPath { get; }

    public AppDbContext()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        DbPath = System.IO.Path.Join(path, "vehicle_data.db"); // Single database file
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure the Id column as an auto-incrementing primary key for Vehicles
        modelBuilder.Entity<Vehicle>()
            .Property(v => v.Id)
            .ValueGeneratedOnAdd();

        // Configure the Id column as an auto-incrementing primary key for ErrorVehicles
        modelBuilder.Entity<ErrorVehicle>()
            .Property(v => v.Id)
            .ValueGeneratedOnAdd();

        // Add unique constraints for both tables
        modelBuilder.Entity<Vehicle>()
            .HasIndex(v => new { v.Vin, v.DealerId, v.ModifiedDate })
            .IsUnique();

        modelBuilder.Entity<ErrorVehicle>()
            .HasIndex(ev => new { ev.Vin, ev.DealerId, ev.ModifiedDate })
            .IsUnique();
    }
}