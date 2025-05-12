namespace Vehicle_Data.Models;

using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Index(nameof(Vin), nameof(DealerId), nameof(ModifiedDate), IsUnique = true)]
public class VehicleModel
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Ensure auto-increment
    public int Id { get; set; } // Primary Key

    public required string Vin { get; set; }
    public required int DealerId { get; set; }
    public required DateOnly ModifiedDate { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
}
