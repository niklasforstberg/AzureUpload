namespace AzureUpload.Controllers;

using AzureUpload.Data;
using AzureUpload.Models;
using AzureUpload.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly IWebHostEnvironment _environment;

    public AuthController(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<AuthController> logger,
        IWebHostEnvironment environment)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _environment = environment;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null)
            {
                return Unauthorized("Invalid username or password");
            }

            if (!VerifyPassword(request.Password, user.PasswordHash))
            {
                return Unauthorized("Invalid username or password");
            }

            var token = GenerateJwtToken(user);

            return new LoginResponse(token, user.Username, user.Role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login attempt for user {Username}", request.Username);
            return StatusCode(500, "An error occurred during login");
        }
    }

    [HttpPost("register")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Register(RegisterUserRequest request)
    {
        try
        {
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            {
                return BadRequest("Username already exists");
            }
            var normalizedRole = request.Role?.ToUpper();
            if (normalizedRole != "ADMIN" && normalizedRole != "USER") 
            {
                return BadRequest("Invalid role specified");
            }

            var hashedPassword = HashPassword(request.Password);

            var user = new User
            {
                Username = request.Username,
                PasswordHash = hashedPassword,
                Role = request.Role
            };

            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("New user registered: {Username} with role {Role}", 
                request.Username, request.Role);

            return Ok("User registered successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering new user {Username}", request.Username);
            return StatusCode(500, "An error occurred during registration");
        }
    }

    [HttpPost("register-admin")]
    public async Task<IActionResult> RegisterInitialAdmin(RegisterUserRequest request)
    {
        // Only allow this endpoint in development
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        try
        {
            // Check if any users exist
            if (await _context.Users.AnyAsync())
            {
                return BadRequest("Initial admin can only be created when no users exist");
            }

            // Force the role to be Admin regardless of what was sent
            var hashedPassword = HashPassword(request.Password);

            var admin = new User
            {
                Username = request.Username,
                PasswordHash = hashedPassword,
                Role = "Admin" // Force admin role
            };

            await _context.Users.AddAsync(admin);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Initial admin user created: {Username}", request.Username);

            return Ok("Initial admin user created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating initial admin user {Username}", request.Username);
            return StatusCode(500, "An error occurred while creating the initial admin user");
        }
    }

    [HttpGet("users")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<IEnumerable<UserResponse>>> GetUsers()
    {
        try
        {
            var users = await _context.Users
                .Select(u => new UserResponse(
                    u.Id,
                    u.Username,
                    u.Role))
                .ToListAsync();

            return Ok(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users list");
            return StatusCode(500, "An error occurred while retrieving users");
        }
    }

    [HttpGet("me")]
    [Authorize(Policy = "UserAccess")]
    public async Task<ActionResult<UserResponse>> GetCurrentUser()
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return BadRequest("User ID claim not found");
            }

            var user = await _context.Users
                .Where(u => u.Id == Guid.Parse(userId))
                .Select(u => new UserResponse(
                    u.Id,
                    u.Username,
                    u.Role))
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return NotFound("User not found");
            }

            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current user information");
            return StatusCode(500, "An error occurred while retrieving user information");
        }
    }

    private string GenerateJwtToken(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddHours(1),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashedBytes);
    }

    private bool VerifyPassword(string password, string hash)
    {
        var hashedPassword = HashPassword(password);
        return hashedPassword == hash;
    }
}
