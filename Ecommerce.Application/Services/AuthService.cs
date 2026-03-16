using BCrypt.Net;
using Ecommerce.Application.DTOs.Auth;
using Ecommerce.Domain.Entities;
using Ecommerce.Domain.Interfaces;
using Ecommerce.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Ecommerce.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<AuthService> _logger;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthService(
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService,
        ILogger<AuthService> logger,
        IRoleRepository roleRepository)
    {
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
        _roleRepository = roleRepository;
    }

    public async Task RegisterAsync(RegisterRequestDto request)
    {
        var email = request.Email.Trim().ToLower();

        var existingUser = await _userRepository.ExistsByEmailAsync(email);

        if (existingUser)
        {
            _logger.LogWarning("Register failed: email already exists {Email}", email);
            throw new ArgumentException("Email already exists");
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var user = new User
        {
            Email = email,
            PasswordHash = passwordHash
        };

        var defaultRole = await _roleRepository.GetByNameAsync("User");

        if (defaultRole == null)
        {
            throw new Exception("Default role 'User' not found");
        }

        user.UserRoles.Add(new UserRole
        {
            RoleId = defaultRole.Id
        });

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        _logger.LogInformation("New user registered {Email}", email);
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        var email = request.Email.Trim().ToLower();

        var user = await _userRepository.GetByEmailAsync(email);

        if (user == null)
        {
            _logger.LogWarning("Login failed: email not found {Email}", email);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        var validPassword = BCrypt.Net.BCrypt.Verify(
            request.Password,
            user.PasswordHash
        );

        if (!validPassword)
        {
            _logger.LogWarning("Login failed: wrong password for {Email}", email);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        var token = _jwtTokenService.GenerateToken(user);

        _logger.LogInformation("User login success {UserId}", user.Id);

        return new LoginResponseDto
        {
            Token = token,
            Id = user.Id,
            Email = user.Email,
            Roles = user.UserRoles
            .Select(ur => ur.Role.Name)
            .ToList()
        };
    }
}