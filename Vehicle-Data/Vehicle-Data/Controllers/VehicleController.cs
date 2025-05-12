using Microsoft.AspNetCore.Mvc;
using Vehicle_Data.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Vehicle_Data.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VehicleController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<VehicleController> _logger;

    public VehicleController(AppDbContext context, ILogger<VehicleController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Imports vehicles from a CSV file in a known location, validates, stores, returns errors/warnings, and archives the file.
    /// </summary>
    /// <returns>Import result with counts and errors</returns>
    [HttpPost("import")]
    public IActionResult ImportVehicles()
    {
        var projectDirectory = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.Parent?.Parent?.Parent?.FullName;
        var csvFilePath = Path.Combine(projectDirectory ?? string.Empty, "Data", "sample-vin-data.csv");
        var archiveDirectory = Path.Combine(projectDirectory ?? string.Empty, "Data", "archive");
        var errors = new List<string>();
        int totalProcessed = 0;
        int successfullyImported = 0;

        if (!System.IO.File.Exists(csvFilePath))
        {
            return NotFound("CSV file not found.");
        }

        try
        {
            using var reader = new StreamReader(csvFilePath);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
            });

            var vehicles = csv.GetRecords<VehicleCsv>().ToList();
            totalProcessed = vehicles.Count;

            foreach (var vehicleCsv in vehicles)
            {
                if (string.IsNullOrWhiteSpace(vehicleCsv.vin) || string.IsNullOrWhiteSpace(vehicleCsv.modifiedDate))
                {
                    errors.Add($"Missing VIN or ModifiedDate for dealerId {vehicleCsv.dealerId}.");
                    continue;
                }
                if (!_context.Vehicles.Any(v => v.Vin == vehicleCsv.vin))
                {
                    try
                    {
                        _context.Vehicles.Add(new VehicleModel
                        {
                            DealerId = vehicleCsv.dealerId,
                            Vin = vehicleCsv.vin,
                            ModifiedDate = DateOnly.Parse(vehicleCsv.modifiedDate),
                            Make = null,
                            Model = null,
                            Year = null,
                        });
                        successfullyImported++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to import VIN {vehicleCsv.vin}: {ex.Message}");
                    }
                }
                else
                {
                    errors.Add($"Duplicate VIN {vehicleCsv.vin} for dealerId {vehicleCsv.dealerId}.");
                }
            }
            _context.SaveChanges();
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error processing CSV: {ex.Message}");
        }

        // Archive the CSV file
        try
        {
            if (!Directory.Exists(archiveDirectory))
                Directory.CreateDirectory(archiveDirectory);
            var archivePath = Path.Combine(archiveDirectory, $"sample-vin-data-{DateTime.Now:yyyyMMddHHmmss}.csv");
            System.IO.File.Move(csvFilePath, archivePath);
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to archive CSV: {ex.Message}");
        }

        return Ok(new
        {
            TotalProcessed = totalProcessed,
            SuccessfullyImported = successfullyImported,
            Errors = errors
        });
    }

    /// <summary>
    /// Augments vehicle data with information from the NHTSA vPIC API.
    /// </summary>
    /// <param name="vin">Vehicle Identification Number</param>
    /// <returns>Updated vehicle details</returns>
    [HttpPost("{vin}/augment")]
    [ProducesResponseType(typeof(VehicleModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AugmentVehicleData(string vin)
    {
        var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.Vin == vin);
        if (vehicle == null)
        {
            return NotFound($"Vehicle with VIN {vin} not found.");
        }

        try
        {
            using var httpClient = new HttpClient();
            var apiUrl = $"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVin/{vin}?format=json";
            var response = await httpClient.GetStringAsync(apiUrl);
            var json = JObject.Parse(response);
            var results = json["Results"];

            var errorCode = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Error Code")?["Value"]?.ToString();
            var errorText = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Error Text")?["Value"]?.ToString();

            if (!string.IsNullOrEmpty(errorCode) && errorCode != "0" && errorText != "1 - Check Digit (9th position) does not calculate properly")
            {
                return StatusCode(500, $"Error from NHTSA API: Code={errorCode}, Text={errorText}");
            }

            vehicle.Make = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Make")?["Value"]?.ToString();
            vehicle.Model = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Model")?["Value"]?.ToString();
            var yearString = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Model Year")?["Value"]?.ToString();
            vehicle.Year = int.TryParse(yearString, out var year) ? year : (int?)null;

            await _context.SaveChangesAsync();
            return Ok(vehicle);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error calling NHTSA API: {ex.Message}");
        }
    }

    /// <summary>
    /// Lists available vehicles with pagination and filtering.
    /// </summary>
    /// <param name="pageNumber">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="dealerId">Optional dealer ID filter</param>
    /// <param name="modifiedDate">Optional date filter for modified date</param>
    /// <returns>Paginated list of vehicles</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<VehicleModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetVehicles(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] int? dealerId = null,
        [FromQuery] DateOnly? modifiedDate = null)
    {
        if (pageNumber < 1 || pageSize < 1)
        {
            return BadRequest("Page number and page size must be greater than 0");
        }

        var query = _context.Vehicles.AsQueryable();

        if (dealerId.HasValue)
        {
            query = query.Where(v => v.DealerId == dealerId.Value);
        }

        if (modifiedDate.HasValue)
        {
            query = query.Where(v => v.ModifiedDate >= modifiedDate.Value);
        }

        var totalItems = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PaginatedResult<VehicleModel>
        {
            Items = items,
            TotalItems = totalItems,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        });
    }

    /// <summary>
    /// Gets a single vehicle record by VIN.
    /// </summary>
    /// <param name="vin">Vehicle Identification Number</param>
    /// <returns>Vehicle details</returns>
    [HttpGet("{vin}")]
    [ProducesResponseType(typeof(VehicleModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVehicle(string vin)
    {
        var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.Vin == vin);
        if (vehicle == null)
        {
            return NotFound($"Vehicle with VIN {vin} not found.");
        }
        return Ok(vehicle);
    }

    /// <summary>
    /// Lists error vehicles with pagination.
    /// </summary>
    /// <param name="pageNumber">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <returns>Paginated list of error vehicles</returns>
    [HttpGet("errors")]
    [ProducesResponseType(typeof(PaginatedResult<ErrorVehicle>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetErrorVehicles(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10)
    {
        if (pageNumber < 1 || pageSize < 1)
        {
            return BadRequest("Page number and page size must be greater than 0");
        }

        var query = _context.ErrorVehicles.AsQueryable();
        var totalItems = await query.CountAsync();
        var items = await query
            .OrderByDescending(ev => ev.ModifiedDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PaginatedResult<ErrorVehicle>
        {
            Items = items,
            TotalItems = totalItems,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        });
    }
}

public class PaginatedResult<T>
{
    public IEnumerable<T> Items { get; set; } = new List<T>();
    public int TotalItems { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
} 