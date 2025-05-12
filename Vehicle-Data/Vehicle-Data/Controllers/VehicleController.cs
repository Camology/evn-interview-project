using Microsoft.AspNetCore.Mvc;
using Vehicle_Data.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Vehicle_Data.Controllers;

/// <summary>
/// Controller for managing vehicle data, including import, augmentation, and error handling.
/// Provides endpoints for CRUD operations on vehicles and error vehicles.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class VehicleController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<VehicleController> _logger;

    /// <summary>
    /// Initializes a new instance of the VehicleController.
    /// </summary>
    /// <param name="context">The database context for vehicle data</param>
    /// <param name="logger">Logger for tracking application events</param>
    public VehicleController(AppDbContext context, ILogger<VehicleController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Imports vehicles from a CSV file in a known location.
    /// Validates each vehicle record, stores valid records, and archives the processed file.
    /// </summary>
    /// <returns>
    /// Import result containing:
    /// - TotalProcessed: Number of records processed
    /// - SuccessfullyImported: Number of records successfully imported
    /// - Errors: List of any errors encountered during import
    /// </returns>
    /// <remarks>
    /// The CSV file should be located in the Data directory and named 'sample-vin-data.csv'.
    /// After processing, the file is moved to the Data/archive directory with a timestamp.
    /// </remarks>
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
    /// Augments a single vehicle's data with information from the NHTSA vPIC API.
    /// </summary>
    /// <param name="vin">Vehicle Identification Number to augment</param>
    /// <returns>
    /// Updated vehicle details including make, model, and year.
    /// Returns 404 if vehicle not found, 500 if API call fails.
    /// </returns>
    /// <remarks>
    /// Calls the NHTSA vPIC API to decode the VIN and update the vehicle's details.
    /// If the API returns an error, the vehicle is moved to the error vehicles table.
    /// </remarks>
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
    /// Augments all vehicles in the database with information from the NHTSA vPIC API.
    /// </summary>
    /// <returns>
    /// Summary of the augmentation process including:
    /// - Number of vehicles successfully updated
    /// - Number of vehicles that encountered errors
    /// </returns>
    /// <remarks>
    /// Processes all vehicles in the database sequentially.
    /// Vehicles that fail validation are moved to the error vehicles table.
    /// </remarks>
    [HttpPost("augment")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AugmentAllVehicles()
    {
        try
        {
            var vehicles = await _context.Vehicles.ToListAsync();
            var updatedCount = 0;
            var errorCount = 0;

            using var httpClient = new HttpClient();
            foreach (var vehicle in vehicles)
            {
                try
                {
                    var apiUrl = $"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVin/{vehicle.Vin}?format=json";
                    var response = await httpClient.GetStringAsync(apiUrl);
                    var json = JObject.Parse(response);
                    var results = json["Results"];

                    var errorCode = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Error Code")?["Value"]?.ToString();
                    var errorText = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Error Text")?["Value"]?.ToString();

                    if (!string.IsNullOrEmpty(errorCode) && errorCode != "0" && errorText != "1 - Check Digit (9th position) does not calculate properly")
                    {
                        _context.Vehicles.Remove(vehicle);
                        _context.ErrorVehicles.Add(new ErrorVehicle
                        {
                            Vin = vehicle.Vin,
                            DealerId = vehicle.DealerId,
                            ModifiedDate = vehicle.ModifiedDate,
                            ErrorCode = errorCode,
                            ErrorText = errorText
                        });
                        errorCount++;
                        continue;
                    }

                    vehicle.Make = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Make")?["Value"]?.ToString();
                    vehicle.Model = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Model")?["Value"]?.ToString();
                    var yearString = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Model Year")?["Value"]?.ToString();
                    vehicle.Year = int.TryParse(yearString, out var year) ? year : (int?)null;
                    updatedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error augmenting vehicle with VIN {vehicle.Vin}");
                    errorCount++;
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = $"Augmentation completed. Updated: {updatedCount}, Errors: {errorCount}" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Error augmenting vehicles: {ex.Message}" });
        }
    }

    /// <summary>
    /// Retrieves a paginated list of vehicles with optional filtering.
    /// </summary>
    /// <param name="pageNumber">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="dealerId">Optional dealer ID filter</param>
    /// <param name="modifiedDate">Optional date filter for modified date</param>
    /// <returns>
    /// Paginated result containing:
    /// - Items: List of vehicles for the current page
    /// - TotalItems: Total number of vehicles matching the filter
    /// - PageNumber: Current page number
    /// - PageSize: Number of items per page
    /// - TotalPages: Total number of pages
    /// </returns>
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
    /// Retrieves a single vehicle by its VIN.
    /// </summary>
    /// <param name="vin">Vehicle Identification Number</param>
    /// <returns>
    /// Vehicle details if found, 404 if not found.
    /// </returns>
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
    /// Retrieves a paginated list of error vehicles.
    /// </summary>
    /// <param name="pageNumber">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <returns>
    /// Paginated result containing:
    /// - Items: List of error vehicles for the current page
    /// - TotalItems: Total number of error vehicles
    /// - PageNumber: Current page number
    /// - PageSize: Number of items per page
    /// - TotalPages: Total number of pages
    /// </returns>
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

    /// <summary>
    /// Retrieves a single error vehicle by its VIN.
    /// </summary>
    /// <param name="vin">Vehicle Identification Number</param>
    /// <returns>
    /// Error vehicle details if found, 404 if not found.
    /// </returns>
    [HttpGet("errors/{vin}")]
    [ProducesResponseType(typeof(ErrorVehicle), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetErrorVehicle(string vin)
    {
        var errorVehicle = await _context.ErrorVehicles.FirstOrDefaultAsync(ev => ev.Vin == vin);
        if (errorVehicle == null)
        {
            return NotFound($"Error vehicle with VIN {vin} not found.");
        }
        return Ok(errorVehicle);
    }

    /// <summary>
    /// Attempts to correct an error vehicle by reprocessing with a corrected VIN.
    /// </summary>
    /// <param name="request">Correction request containing:
    /// - OriginalVin: The VIN that had an error
    /// - CorrectedVin: The new VIN to try
    /// - DealerId: The dealer ID associated with the vehicle
    /// - ModifiedDate: The date the vehicle was modified
    /// </param>
    /// <returns>
    /// Success message if correction successful,
    /// Error details if correction fails,
    /// 404 if error vehicle not found.
    /// </returns>
    /// <remarks>
    /// If the correction is successful, the error vehicle is removed and a new vehicle is added.
    /// If the correction fails, the old error vehicle is removed and a new error vehicle is added.
    /// </remarks>
    [HttpPost("correct-error")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CorrectErrorVehicle([FromBody] CorrectErrorRequest request)
    {
        var errorVehicle = await _context.ErrorVehicles.FirstOrDefaultAsync(ev => ev.Vin == request.OriginalVin);
        if (errorVehicle == null)
        {
            return NotFound($"Error vehicle with VIN {request.OriginalVin} not found.");
        }

        try
        {
            using var httpClient = new HttpClient();
            var apiUrl = $"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVin/{request.CorrectedVin}?format=json";
            var response = await httpClient.GetStringAsync(apiUrl);
            var json = JObject.Parse(response);
            var results = json["Results"];

            var errorCode = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Error Code")?["Value"]?.ToString();
            var errorText = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Error Text")?["Value"]?.ToString();

            if (!string.IsNullOrEmpty(errorCode) && errorCode != "0" && errorText != "1 - Check Digit (9th position) does not calculate properly")
            {
                // Remove the old error vehicle
                _context.ErrorVehicles.Remove(errorVehicle);

                // Add the new error vehicle with the corrected VIN
                _context.ErrorVehicles.Add(new ErrorVehicle
                {
                    Vin = request.CorrectedVin,
                    DealerId = request.DealerId,
                    ModifiedDate = DateOnly.Parse(request.ModifiedDate),
                    ErrorCode = errorCode,
                    ErrorText = errorText
                });

                await _context.SaveChangesAsync();
                return BadRequest(new { message = $"Error from NHTSA API: Code={errorCode}, Text={errorText}" });
            }

            // If successful, remove the error vehicle and add a new vehicle
            _context.ErrorVehicles.Remove(errorVehicle);
            _context.Vehicles.Add(new VehicleModel
            {
                Vin = request.CorrectedVin,
                DealerId = request.DealerId,
                ModifiedDate = DateOnly.Parse(request.ModifiedDate),
                Make = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Make")?["Value"]?.ToString(),
                Model = results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Model")?["Value"]?.ToString(),
                Year = int.TryParse(results?.FirstOrDefault(r => r["Variable"]?.ToString() == "Model Year")?["Value"]?.ToString(), out var year) ? year : null
            });

            await _context.SaveChangesAsync();
            return Ok(new { message = "VIN correction successful" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Error processing correction: {ex.Message}" });
        }
    }
}

/// <summary>
/// Represents a paginated result set of any type.
/// </summary>
/// <typeparam name="T">The type of items in the result set</typeparam>
public class PaginatedResult<T>
{
    /// <summary>
    /// The items for the current page
    /// </summary>
    public IEnumerable<T> Items { get; set; } = new List<T>();

    /// <summary>
    /// Total number of items across all pages
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages { get; set; }
}

/// <summary>
/// Request model for correcting an error vehicle.
/// </summary>
public class CorrectErrorRequest
{
    /// <summary>
    /// The original VIN that had an error
    /// </summary>
    public string OriginalVin { get; set; } = string.Empty;

    /// <summary>
    /// The corrected VIN to try
    /// </summary>
    public string CorrectedVin { get; set; } = string.Empty;

    /// <summary>
    /// The dealer ID associated with the vehicle
    /// </summary>
    public int DealerId { get; set; }

    /// <summary>
    /// The date the vehicle was modified
    /// </summary>
    public string ModifiedDate { get; set; } = string.Empty;
} 