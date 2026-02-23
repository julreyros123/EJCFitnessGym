using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EJCFitnessGym.Data;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EJCFitnessGym.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public sealed class AuthTokenController : ControllerBase
    {
        private const string DevelopmentFallbackSigningKey =
            "dev-only-jwt-signing-key-change-this-before-production-32-bytes-min";
        private const string RefreshTokenLoginProvider = "EJC.RefreshToken";

        private readonly ApplicationDbContext _db;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly JwtOptions _jwtOptions;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<AuthTokenController> _logger;

        public AuthTokenController(
            ApplicationDbContext db,
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            IOptions<JwtOptions> jwtOptions,
            IHostEnvironment environment,
            ILogger<AuthTokenController> logger)
        {
            _db = db;
            _signInManager = signInManager;
            _userManager = userManager;
            _jwtOptions = jwtOptions.Value;
            _environment = environment;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpPost("token")]
        public async Task<IActionResult> IssueToken([FromBody] TokenRequest request, CancellationToken cancellationToken)
        {
            var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { error = "Email and password are required." });
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user is null || !await _userManager.HasPasswordAsync(user))
            {
                return Unauthorized(new { error = "Invalid credentials." });
            }

            var signInResult = await _signInManager.CheckPasswordSignInAsync(
                user,
                request.Password,
                lockoutOnFailure: true);

            if (!signInResult.Succeeded)
            {
                if (signInResult.IsLockedOut)
                {
                    return Unauthorized(new { error = "Account is locked. Please try again later." });
                }

                if (signInResult.IsNotAllowed)
                {
                    return Unauthorized(new { error = "Account is not allowed to sign in." });
                }

                return Unauthorized(new { error = "Invalid credentials." });
            }

            var (roles, branchIds) = await ResolveRolesAndBranchesAsync(user);
            if (!HasRequiredRole(roles, request.RequiredRole))
            {
                return Forbid();
            }

            var accessTokenResult = CreateAccessToken(user, email, roles, branchIds);
            if (accessTokenResult is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = "JWT signing key is not configured."
                });
            }

            await PruneRefreshTokensAsync(user.Id, DateTime.UtcNow, cancellationToken);
            var refreshTokenResult = await CreateRefreshTokenAsync(user);
            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new TokenResponse
            {
                TokenType = "Bearer",
                AccessToken = accessTokenResult.AccessToken,
                ExpiresAtUtc = accessTokenResult.ExpiresAtUtc,
                RefreshToken = refreshTokenResult.RawToken,
                RefreshTokenExpiresAtUtc = refreshTokenResult.ExpiresAtUtc,
                UserId = user.Id,
                Email = user.Email ?? email,
                Roles = roles,
                BranchIds = branchIds
            });
        }

        [AllowAnonymous]
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
        {
            if (!TryParseRefreshToken(request.RefreshToken, out var tokenId, out var rawToken))
            {
                return Unauthorized(new { error = "Invalid refresh token." });
            }

            var tokenRow = await _db.Set<IdentityUserToken<string>>()
                .FirstOrDefaultAsync(token =>
                    token.LoginProvider == RefreshTokenLoginProvider &&
                    token.Name == tokenId,
                    cancellationToken);

            if (tokenRow is null || !TryDeserializeRefreshTokenState(tokenRow.Value, out var tokenState))
            {
                return Unauthorized(new { error = "Refresh token is invalid." });
            }

            if (!ValidateRefreshTokenHash(tokenState.TokenHash, rawToken))
            {
                return Unauthorized(new { error = "Refresh token is invalid." });
            }

            var nowUtc = DateTime.UtcNow;
            if (tokenState.RevokedAtUtc.HasValue || tokenState.ExpiresAtUtc <= nowUtc)
            {
                return Unauthorized(new { error = "Refresh token is expired or revoked." });
            }

            var user = await _userManager.FindByIdAsync(tokenRow.UserId);
            if (user is null)
            {
                tokenState.RevokedAtUtc = nowUtc;
                tokenRow.Value = SerializeRefreshTokenState(tokenState);
                await _db.SaveChangesAsync(cancellationToken);
                return Unauthorized(new { error = "Refresh token is invalid." });
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                return Unauthorized(new { error = "Account is locked. Please sign in again later." });
            }

            var (roles, branchIds) = await ResolveRolesAndBranchesAsync(user);
            if (!HasRequiredRole(roles, request.RequiredRole))
            {
                return Forbid();
            }

            var accessTokenResult = CreateAccessToken(user, user.Email ?? user.UserName ?? user.Id, roles, branchIds);
            if (accessTokenResult is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = "JWT signing key is not configured."
                });
            }

            tokenState.RevokedAtUtc = nowUtc;
            tokenState.LastUsedAtUtc = nowUtc;
            var replacement = await CreateRefreshTokenAsync(user);
            tokenState.ReplacedByTokenId = replacement.TokenId;
            tokenRow.Value = SerializeRefreshTokenState(tokenState);

            await PruneRefreshTokensAsync(user.Id, nowUtc, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new TokenResponse
            {
                TokenType = "Bearer",
                AccessToken = accessTokenResult.AccessToken,
                ExpiresAtUtc = accessTokenResult.ExpiresAtUtc,
                RefreshToken = replacement.RawToken,
                RefreshTokenExpiresAtUtc = replacement.ExpiresAtUtc,
                UserId = user.Id,
                Email = user.Email ?? user.UserName ?? user.Id,
                Roles = roles,
                BranchIds = branchIds
            });
        }

        [AllowAnonymous]
        [HttpPost("revoke")]
        public async Task<IActionResult> Revoke([FromBody] RevokeTokenRequest request, CancellationToken cancellationToken)
        {
            if (!TryParseRefreshToken(request.RefreshToken, out var tokenId, out _))
            {
                return Ok(new { revoked = false });
            }

            var tokenRow = await _db.Set<IdentityUserToken<string>>()
                .FirstOrDefaultAsync(token =>
                    token.LoginProvider == RefreshTokenLoginProvider &&
                    token.Name == tokenId,
                    cancellationToken);

            if (tokenRow is null || !TryDeserializeRefreshTokenState(tokenRow.Value, out var tokenState))
            {
                return Ok(new { revoked = false });
            }

            if (!tokenState.RevokedAtUtc.HasValue)
            {
                tokenState.RevokedAtUtc = DateTime.UtcNow;
                tokenRow.Value = SerializeRefreshTokenState(tokenState);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Ok(new { revoked = true });
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult GetCurrentIdentity()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var email = User.FindFirstValue(JwtRegisteredClaimNames.Email) ?? User.FindFirstValue(ClaimTypes.Email);
            var roles = User.FindAll(ClaimTypes.Role)
                .Select(claim => claim.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var branchIds = User.FindAll(BranchAccess.BranchIdClaimType)
                .Select(claim => claim.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Ok(new
            {
                userId,
                email,
                roles,
                branchIds,
                authenticationType = User.Identity?.AuthenticationType
            });
        }

        private async Task<(string[] Roles, List<string> BranchIds)> ResolveRolesAndBranchesAsync(IdentityUser user)
        {
            var roles = (await _userManager.GetRolesAsync(user))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var userClaims = await _userManager.GetClaimsAsync(user);
            var branchIds = userClaims
                .Where(claim =>
                    claim.Type == BranchAccess.BranchIdClaimType &&
                    !string.IsNullOrWhiteSpace(claim.Value))
                .Select(claim => claim.Value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(branchId => branchId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return (roles, branchIds);
        }

        private bool HasRequiredRole(IReadOnlyCollection<string> roles, string? requiredRole)
        {
            if (string.IsNullOrWhiteSpace(requiredRole))
            {
                return true;
            }

            return roles.Any(role => string.Equals(role, requiredRole, StringComparison.OrdinalIgnoreCase));
        }

        private AccessTokenResult? CreateAccessToken(
            IdentityUser user,
            string normalizedEmail,
            IReadOnlyCollection<string> roles,
            IReadOnlyCollection<string> branchIds)
        {
            var signingKey = ResolveSigningKey();
            if (string.IsNullOrWhiteSpace(signingKey))
            {
                _logger.LogError("JWT signing key is not configured.");
                return null;
            }

            var nowUtc = DateTime.UtcNow;
            var expiresUtc = nowUtc.AddMinutes(Math.Clamp(_jwtOptions.AccessTokenMinutes, 5, 24 * 60));

            var tokenClaims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(JwtRegisteredClaimNames.Email, user.Email ?? normalizedEmail),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id)
            };

            foreach (var role in roles)
            {
                tokenClaims.Add(new Claim(ClaimTypes.Role, role));
            }

            foreach (var branchId in branchIds)
            {
                tokenClaims.Add(new Claim(BranchAccess.BranchIdClaimType, branchId));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(tokenClaims),
                Expires = expiresUtc,
                NotBefore = nowUtc,
                IssuedAt = nowUtc,
                Issuer = ResolveIssuer(),
                Audience = ResolveAudience(),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var securityToken = tokenHandler.CreateToken(tokenDescriptor);
            return new AccessTokenResult
            {
                AccessToken = tokenHandler.WriteToken(securityToken),
                ExpiresAtUtc = expiresUtc
            };
        }

        private Task<RefreshTokenResult> CreateRefreshTokenAsync(IdentityUser user)
        {
            var tokenId = Guid.NewGuid().ToString("N");
            var secret = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
            var rawToken = $"{tokenId}.{secret}";

            var nowUtc = DateTime.UtcNow;
            var expiresUtc = nowUtc.AddDays(Math.Clamp(_jwtOptions.RefreshTokenDays, 1, 90));
            var tokenState = new RefreshTokenState
            {
                TokenHash = ComputeTokenHash(rawToken),
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = expiresUtc,
                CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()
            };

            _db.Set<IdentityUserToken<string>>().Add(new IdentityUserToken<string>
            {
                UserId = user.Id,
                LoginProvider = RefreshTokenLoginProvider,
                Name = tokenId,
                Value = SerializeRefreshTokenState(tokenState)
            });

            return Task.FromResult(new RefreshTokenResult
            {
                TokenId = tokenId,
                RawToken = rawToken,
                ExpiresAtUtc = expiresUtc
            });
        }

        private async Task PruneRefreshTokensAsync(string userId, DateTime nowUtc, CancellationToken cancellationToken)
        {
            var retentionDays = Math.Clamp(_jwtOptions.RevokedTokenRetentionDays, 1, 365);
            var revokedRetentionCutoffUtc = nowUtc.AddDays(-retentionDays);
            var tokenRows = await _db.Set<IdentityUserToken<string>>()
                .Where(token => token.UserId == userId && token.LoginProvider == RefreshTokenLoginProvider)
                .ToListAsync(cancellationToken);

            if (tokenRows.Count == 0)
            {
                return;
            }

            var changed = false;
            var activeTokenRows = new List<(IdentityUserToken<string> Row, RefreshTokenState State)>();

            foreach (var row in tokenRows)
            {
                if (!TryDeserializeRefreshTokenState(row.Value, out var state))
                {
                    _db.Remove(row);
                    changed = true;
                    continue;
                }

                var isExpired = state.ExpiresAtUtc <= nowUtc;
                var isRevoked = state.RevokedAtUtc.HasValue;
                var revokedAtUtc = state.RevokedAtUtc;
                var removeRow = isExpired || (revokedAtUtc.HasValue && revokedAtUtc.Value <= revokedRetentionCutoffUtc);

                if (removeRow)
                {
                    _db.Remove(row);
                    changed = true;
                    continue;
                }

                if (!isRevoked && !isExpired)
                {
                    activeTokenRows.Add((row, state));
                }
            }

            var maxActive = Math.Clamp(_jwtOptions.MaxActiveRefreshTokensPerUser, 1, 20);
            if (activeTokenRows.Count > maxActive)
            {
                foreach (var (row, state) in activeTokenRows
                    .OrderByDescending(item => item.State.CreatedAtUtc)
                    .Skip(maxActive))
                {
                    state.RevokedAtUtc = nowUtc;
                    row.Value = SerializeRefreshTokenState(state);
                    changed = true;
                }
            }

            if (changed)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        private static bool TryParseRefreshToken(string? rawToken, out string tokenId, out string normalizedRawToken)
        {
            tokenId = string.Empty;
            normalizedRawToken = string.Empty;

            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return false;
            }

            var candidate = rawToken.Trim();
            var separatorIndex = candidate.IndexOf('.');
            if (separatorIndex <= 0 || separatorIndex >= candidate.Length - 1)
            {
                return false;
            }

            tokenId = candidate[..separatorIndex];
            var secret = candidate[(separatorIndex + 1)..];
            if (string.IsNullOrWhiteSpace(tokenId) || string.IsNullOrWhiteSpace(secret))
            {
                return false;
            }

            normalizedRawToken = candidate;
            return true;
        }

        private static string ComputeTokenHash(string rawToken)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(hashBytes);
        }

        private static bool ValidateRefreshTokenHash(string storedHash, string rawToken)
        {
            var candidateHash = ComputeTokenHash(rawToken);
            var left = Encoding.UTF8.GetBytes(storedHash);
            var right = Encoding.UTF8.GetBytes(candidateHash);
            return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
        }

        private static string SerializeRefreshTokenState(RefreshTokenState state)
        {
            return JsonSerializer.Serialize(state);
        }

        private static bool TryDeserializeRefreshTokenState(string? rawValue, out RefreshTokenState state)
        {
            state = new RefreshTokenState();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<RefreshTokenState>(rawValue);
                if (parsed is null || string.IsNullOrWhiteSpace(parsed.TokenHash))
                {
                    return false;
                }

                state = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private string? ResolveSigningKey()
        {
            var configuredKey = _jwtOptions.SigningKey?.Trim();
            if (!string.IsNullOrWhiteSpace(configuredKey))
            {
                return configuredKey;
            }

            return _environment.IsDevelopment() ? DevelopmentFallbackSigningKey : null;
        }

        private string ResolveIssuer()
        {
            return string.IsNullOrWhiteSpace(_jwtOptions.Issuer)
                ? "EJCFitnessGym"
                : _jwtOptions.Issuer.Trim();
        }

        private string ResolveAudience()
        {
            return string.IsNullOrWhiteSpace(_jwtOptions.Audience)
                ? "EJCFitnessGymClients"
                : _jwtOptions.Audience.Trim();
        }

        public sealed class TokenRequest
        {
            public string? Email { get; set; }
            public string? Password { get; set; }
            public string? RequiredRole { get; set; }
        }

        public sealed class RefreshTokenRequest
        {
            public string? RefreshToken { get; set; }
            public string? RequiredRole { get; set; }
        }

        public sealed class RevokeTokenRequest
        {
            public string? RefreshToken { get; set; }
        }

        public sealed class TokenResponse
        {
            public string TokenType { get; set; } = "Bearer";
            public string AccessToken { get; set; } = string.Empty;
            public DateTime ExpiresAtUtc { get; set; }
            public string RefreshToken { get; set; } = string.Empty;
            public DateTime RefreshTokenExpiresAtUtc { get; set; }
            public string UserId { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> BranchIds { get; set; } = Array.Empty<string>();
        }

        private sealed class AccessTokenResult
        {
            public string AccessToken { get; init; } = string.Empty;
            public DateTime ExpiresAtUtc { get; init; }
        }

        private sealed class RefreshTokenResult
        {
            public string TokenId { get; init; } = string.Empty;
            public string RawToken { get; init; } = string.Empty;
            public DateTime ExpiresAtUtc { get; init; }
        }

        private sealed class RefreshTokenState
        {
            public string TokenHash { get; set; } = string.Empty;
            public DateTime CreatedAtUtc { get; set; }
            public DateTime ExpiresAtUtc { get; set; }
            public DateTime? RevokedAtUtc { get; set; }
            public DateTime? LastUsedAtUtc { get; set; }
            public string? ReplacedByTokenId { get; set; }
            public string? CreatedByIp { get; set; }
            public string? UserAgent { get; set; }
        }
    }
}
