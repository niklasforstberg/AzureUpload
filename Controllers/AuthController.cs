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

            var normalizedRole = request.Role?.Trim().ToUpper();
            
            if (normalizedRole != "ADMIN" && normalizedRole != "USER") 
            {
                return BadRequest("Invalid role specified");
            }

            var hashedPassword = HashPassword(request.Password);

            var user = new User
            {
                Username = request.Username,
                PasswordHash = hashedPassword,
                Role = normalizedRole
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
                Role = "ADMIN"
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

    [HttpGet("development-token")]
    public IActionResult GetDevelopmentToken()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);

        var devUserId = "08586847-E00A-4BA8-AE6C-541CB15C6AD0"; //me in da db

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "nik"),
                new Claim(ClaimTypes.Role, "ADMIN"),
                new Claim(ClaimTypes.NameIdentifier, devUserId)
            }),
            Expires = DateTime.UtcNow.AddYears(1),
            Issuer = _configuration["Jwt:Issuer"],
            Audience = _configuration["Jwt:Audience"],
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return Ok(new
        {
            Token = tokenString,
            ExpiresIn = "1 year",
            TokenType = "Bearer",
            Note = "This token is for development purposes only!",
            SwaggerCopyPaste = $"Bearer {tokenString}"
        });
    }

    [HttpGet("development-token-user")]
    public IActionResult GetDevelopmentTokenUser()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);

        var devUserId = "14A317B6-54AB-439D-A031-DAA707E3748D"; // Kims db ID

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "kim"),
                new Claim(ClaimTypes.Role, "USER"),
                new Claim(ClaimTypes.NameIdentifier, devUserId)
            }),
            Expires = DateTime.UtcNow.AddYears(1),
            Issuer = _configuration["Jwt:Issuer"],
            Audience = _configuration["Jwt:Audience"],
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return Ok(new
        {
            Token = tokenString,
            ExpiresIn = "1 year",
            TokenType = "Bearer",
            Note = "This token is for development purposes only!",
            SwaggerCopyPaste = $"Bearer {tokenString}"
        });
    }

    [HttpDelete("users/{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        try
        {
            var user = await _context.Users.FindAsync(id);
            
            if (user == null)
            {
                return NotFound("User not found");
            }

            // Prevent deleting the last admin user
            if (user.Role == "ADMIN")
            {
                var adminCount = await _context.Users.CountAsync(u => u.Role == "ADMIN");
                if (adminCount <= 1)
                {
                    return BadRequest("Cannot delete the last admin user");
                }
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User deleted: {Username} (ID: {Id})", user.Username, user.Id);
            
            return Ok("User deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user with ID {UserId}", id);
            return StatusCode(500, "An error occurred while deleting the user");
        }
    }

    [HttpPut("change-password")]
    [Authorize(Policy = "UserAccess")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return BadRequest("User ID claim not found");
            }

            var user = await _context.Users.FindAsync(Guid.Parse(userId));
            if (user == null)
            {
                return NotFound("User not found");
            }

            // Verify current password
            if (!VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                return BadRequest("Current password is incorrect");
            }

            // Update password
            user.PasswordHash = HashPassword(request.NewPassword);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Password changed for user: {Username}", user.Username);
            return Ok("Password changed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password for user");
            return StatusCode(500, "An error occurred while changing the password");
        }
    }

    [HttpPut("change-username")]
    [Authorize(Policy = "UserAccess")]
    public async Task<IActionResult> ChangeUsername(ChangeUsernameRequest request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return BadRequest("User ID claim not found");
            }

            var user = await _context.Users.FindAsync(Guid.Parse(userId));
            if (user == null)
            {
                return NotFound("User not found");
            }

            // Check if new username is already taken
            if (await _context.Users.AnyAsync(u => u.Username == request.NewUsername && u.Id != user.Id))
            {
                return BadRequest("Username is already taken");
            }

            // Update username
            user.Username = request.NewUsername;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Username changed for user ID {UserId} from {OldUsername} to {NewUsername}", 
                user.Id, user.Username, request.NewUsername);
            
            // Generate new token with updated username
            var newToken = GenerateJwtToken(user);
            
            return Ok(new { 
                Message = "Username changed successfully",
                NewToken = newToken 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing username");
            return StatusCode(500, "An error occurred while changing the username");
        }
    }

    [HttpPut("admin/change-password")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AdminChangePassword(AdminChangePasswordRequest request)
    {
        try
        {
            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null)
            {
                return NotFound("User not found");
            }

            // Update password
            user.PasswordHash = HashPassword(request.NewPassword);
            await _context.SaveChangesAsync();

            var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _logger.LogInformation("Admin {AdminId} changed password for user: {Username}", adminId, user.Username);
            return Ok("Password changed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in admin password change");
            return StatusCode(500, "An error occurred while changing the password");
        }
    }

    [HttpPut("admin/change-username")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AdminChangeUsername(AdminChangeUsernameRequest request)
    {
        try
        {
            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null)
            {
                return NotFound("User not found");
            }

            // Check if new username is already taken
            if (await _context.Users.AnyAsync(u => u.Username == request.NewUsername && u.Id != user.Id))
            {
                return BadRequest("Username is already taken");
            }

            var oldUsername = user.Username;
            user.Username = request.NewUsername;
            await _context.SaveChangesAsync();

            var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _logger.LogInformation("Admin {AdminId} changed username for user {UserId} from {OldUsername} to {NewUsername}", 
                adminId, user.Id, oldUsername, request.NewUsername);
            
            return Ok("Username changed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in admin username change");
            return StatusCode(500, "An error occurred while changing the username");
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
