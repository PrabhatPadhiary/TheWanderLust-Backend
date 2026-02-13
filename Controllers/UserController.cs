using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Helpers;
using System.Text;
using System.Text.RegularExpressions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using System.Security.Cryptography;
using TheWanderLustWebAPI.Models.Dto;

namespace TheWanderLustWebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _authContext;
        private readonly IWebHostEnvironment _env;
        private readonly CloudinaryService _cloudinaryService;
        public UserController(AppDbContext appDbContext, IWebHostEnvironment env, CloudinaryService cloudinaryService)
        {
            _authContext = appDbContext;
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _cloudinaryService = cloudinaryService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Authenticate([FromBody] User userObj)
        {
            if (userObj == null)
                return BadRequest();

            var user = await _authContext.Users.FirstOrDefaultAsync(x => x.Username == userObj.Username);

            if (user == null)
                return BadRequest("User Doesn't Exist. Please SignUp");
            if (!PasswordHasher.Verifypassword(userObj.Password, user.Password))
                return BadRequest("Incorrect Username/Password");

            user.Token = CreateJwt(user);
            var newAccessToken = user.Token;
            var newRefreshToken = CreateRefreshToken();
            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1);
            await _authContext.SaveChangesAsync();

            return Ok(new LoginApiDto()
            {
                RefreshToken = newRefreshToken,
                AccessToken = newAccessToken
            });

        }

        [HttpPost("signup")]
        public async Task<IActionResult> RegisterUser([FromForm] UserSignUpDto userDto)
        {
            if (userDto == null)
            {
                return BadRequest();
            }
            if (await CheckUserNameExistsAsync(userDto.Username))
            {
                return BadRequest("Username Already Taken");
            }
            if (await CheckEmailExistsAsync(userDto.Email))
            {
                return BadRequest("Email Already Registered");
            }
            var pass = CheckPasswordStrength(userDto.Password);
            if (!string.IsNullOrEmpty(pass))
            {
                return BadRequest(pass);
            }

            string? profilePicUrl = null;

            if (userDto.ProfilePicture != null)
            {
                profilePicUrl = await _cloudinaryService
                    .UploadImageAsync(userDto.ProfilePicture, "profile_pictures");
            }


            var user = new User
            {
                FirstName = userDto.FirstName,
                LastName = userDto.LastName,
                Email = userDto.Email,
                Username = userDto.Username,
                Password = PasswordHasher.HashPassword(userDto.Password),
                Role = "User",
                Token = "",
                ProfilePictureUrl = profilePicUrl,
                RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7)
            };

            await _authContext.Users.AddAsync(user);
            await _authContext.SaveChangesAsync();

            return Ok(new
            {
                Message = "User registered Successfully"
            });
        }

        [Authorize]
        [HttpGet("getAllUsers")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _authContext.Users.ToListAsync();
            return Ok(users);
        }

        [HttpGet("top-writers")]
        public async Task<IActionResult> GetTopBlogWriters()
        {
            var topWriters = await _authContext.Users
                .Select(user => new
                {
                    Email = user.Email,
                    Username = user.Username,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    ProfilePicUrl = user.ProfilePictureUrl,
                    BlogCount = _authContext.Blogs.Count(b => b.UserEmail == user.Email)
                })
                .OrderByDescending(u => u.BlogCount)
                .ToListAsync();

            return Ok(topWriters);
        }

        private Task<bool> CheckUserNameExistsAsync(string username)
        => _authContext.Users.AnyAsync(x => x.Username == username);

        private Task<bool> CheckEmailExistsAsync(string email)
        => _authContext.Users.AnyAsync(x => x.Email == email);

        private string CheckPasswordStrength(string password)
        {
            StringBuilder sb = new StringBuilder();
            if (password.Length < 8)
                sb.Append("Minimum password length should be 8" + Environment.NewLine);
            if (!(Regex.IsMatch(password, "[a-z]") && Regex.IsMatch(password, "[A-Z]") && Regex.IsMatch(password, "[0-9]")))
                sb.Append("Password should be AlphaNumeric" + Environment.NewLine);
            if (!Regex.IsMatch(password, "[<,>,@,!,#,$,%,^,&,*,(,),_,+,\\[,\\],{,},?,:,;,|,',\\,.,/,~,`,-,=]"))
                sb.Append("Password should contain special characters" + Environment.NewLine);

            return sb.ToString();
        }

        private string CreateRefreshToken()
        {
            var tokenBytes = RandomNumberGenerator.GetBytes(64);
            var refreshToken = Convert.ToBase64String(tokenBytes);

            var tokenInUser = _authContext.Users.Any(x => x.RefreshToken == refreshToken);
            if (tokenInUser)
            {
                return CreateRefreshToken();
            }
            return refreshToken;
        }

        private ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            var key = Encoding.ASCII.GetBytes("veryverysecret.....");
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateLifetime = false
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            SecurityToken securityToken;

            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out securityToken);
            var jwtSecurityToken = securityToken as JwtSecurityToken;

            if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                throw new SecurityTokenException("This is invalid token");
            return principal;
        }

        private string CreateJwt(User user)
        {
            var jwtTokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes("veryverysecret.....");
            var identity = new ClaimsIdentity(new Claim[]{
                new Claim("firstName", user.FirstName),
                new Claim("lastName", user.LastName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(ClaimTypes.Name, user.Username)
            });

            var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = identity,
                Expires = DateTime.Now.AddMinutes(45),
                SigningCredentials = credentials
            };

            var token = jwtTokenHandler.CreateToken(tokenDescriptor);
            return jwtTokenHandler.WriteToken(token);
        }

        [HttpPost("refreshTokens")]
        public async Task<IActionResult> refreshTokens(LoginApiDto loginApiDto)
        {
            if (loginApiDto is null)
                return BadRequest("Invalid Client Request");
            string accessToken = loginApiDto.AccessToken;
            string refreshToken = loginApiDto.RefreshToken;

            var principal = GetPrincipalFromExpiredToken(accessToken);
            var username = principal.Identity.Name;

            var user = await _authContext.Users.FirstOrDefaultAsync(x => x.Username == username);
            if (user is null || user.RefreshToken != refreshToken || user.RefreshTokenExpiryTime <= DateTime.Now)
                return BadRequest("Invalid Request");

            var newAccessToken = CreateJwt(user);
            var newRefreshToken = CreateRefreshToken();

            user.RefreshToken = newRefreshToken;
            await _authContext.SaveChangesAsync();

            return Ok(new LoginApiDto
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
            });
        }

        [HttpGet("getUserDetails")]
        public IActionResult GetUserDetails([FromQuery] string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return BadRequest("Email Parameter is required");
            }

            var user = _authContext.Users.FirstOrDefault(x => x.Email.ToLower() == email.ToLower());
            if (user == null)
            {
                return NotFound(new { Message = "User not found." });
            }

            return Ok(new UserDetailsRespDto
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                ProfilePicUrl = user.ProfilePictureUrl,
                Username = user.Username,
            });
        }

    }
}