using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TypiQLDebug.Models;
using TypiQLDebug.Models.Mongo;
using TypiQLDebug.Models.Mongo.Types;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;

namespace TypiQLDebug.Services
{
    public interface IUserService
    {
        Task<User> Register(User user);
        Task<User> UpdatePassword(string username, string oldPassword, string newPassword);
        Task<User> Authenticate(string username, string password);
        Task<User> Refresh(string token, string refreshToken);
    }

    public class UserService : IUserService
    {

        private readonly Settings _appSettings;
        private readonly MongoContext _mongoContext;
        private readonly JwtSecurityTokenHandler _tokenHandler;


        public UserService(IOptions<Settings> appSettings)
        {
            _appSettings = appSettings.Value;
            _mongoContext = new MongoContext(_appSettings.ConnectionString, _appSettings.Database);
            _tokenHandler = new JwtSecurityTokenHandler();
        }
        private async Task<List<Claim>> BuildClaims(User user)
        {
            List<Claim> claims = new List<Claim>();
            claims.Add(new Claim(ClaimTypes.Name, user.userName.ToString()));
            if (user.admin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }
            UserADMock uadm = await _mongoContext.UsersADMock.Find(Builders<UserADMock>.Filter.Eq(s => s.sAMAccountName, user.userName)).FirstOrDefaultAsync();
            List<GroupADMock> groups = await _mongoContext.GroupsADMock.Find(_ => true).ToListAsync();
            foreach(GroupADMock group in groups)
            {
                if (uadm.memberOf.Contains(group.distinguishedName))
                    claims.Add(new Claim(ClaimTypes.Role, group.sAMAccountName));
            }
            return claims;
        }
        public async Task<User> Register(User user)
        {
            var password = user.password;
            var existingUser = await _mongoContext.Users.Find(Builders<User>.Filter.And(
                 Builders<User>.Filter.Eq(u => u.userName, user.userName)
               )
             ).FirstOrDefaultAsync();
            if (user == null)
                return null;
            user._id = ObjectId.GenerateNewId();
            byte[] salt = new byte[128 / 8];
            user.salt = salt;
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            string hashed = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: user.password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA1,
                iterationCount: 10000,
                numBytesRequested: 256 / 8));
            user.password = hashed;
            await _mongoContext.Users.InsertOneAsync(user);
            return await Authenticate(user.userName, password);
        }
        public async Task<User> UpdatePassword(string username, string oldPassword, string newPassword)
        {
            if (await Authenticate(username, oldPassword) != null)
            {
                byte[] salt = new byte[128 / 8];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                }
                string hashed = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                    password: newPassword,
                    salt: salt,
                    prf: KeyDerivationPrf.HMACSHA1,
                    iterationCount: 10000,
                    numBytesRequested: 256 / 8));
                _mongoContext.Users.UpdateOne(
                  Builders<User>.Filter.Eq(u => u.userName, username),
                  Builders<User>.Update.Set(u => u.password, hashed).Set(u => u.salt, salt));
                return await Authenticate(username, newPassword);
            }
            else return null;

        }

        public async Task<User> Authenticate(string username, string password)
        {
            var user = await _mongoContext.Users.Find(Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(u => u.userName, username)
              )
            ).FirstOrDefaultAsync();
            if (user == null)
                return null;
            if (user != null)
            {
                string hashed = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                    password: password,
                    salt: user.salt,
                    prf: KeyDerivationPrf.HMACSHA1,
                    iterationCount: 10000,
                    numBytesRequested: 256 / 8));
                if (hashed != user.password)
                    return null;
            }

            var key = Encoding.ASCII.GetBytes(_appSettings.Secret);
            List<Claim> claims = await BuildClaims(user);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Issuer = "data-crush.com",
                Audience = "data-crush.com",
                Subject = new ClaimsIdentity(claims.ToArray()),
                Expires = DateTime.UtcNow.AddMinutes(10),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = _tokenHandler.CreateToken(tokenDescriptor);
            user.token = _tokenHandler.WriteToken(token);
            user.tokenExpires = tokenDescriptor.Expires.GetValueOrDefault();
            var refreshDescriptor = new SecurityTokenDescriptor
            {
                Issuer = "data-crush.com",
                Audience = "data-crush.com",
                Expires = DateTime.UtcNow.AddDays(30),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var refreshToken = _tokenHandler.CreateToken(refreshDescriptor);
            user.refreshToken = _tokenHandler.WriteToken(refreshToken);

            return user.WithoutPassword();
        }



        public async Task<User> Refresh(string token, string refreshToken)
        {
            var key = Encoding.ASCII.GetBytes(_appSettings.Secret);
            ClaimsPrincipal principal = _tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidAudience = "data-crush.com",
                ValidIssuer = "data-crush.com",
                IssuerSigningKey = new SymmetricSecurityKey(key)
            }, out SecurityToken oldAccessToken);
            _tokenHandler.ValidateToken(refreshToken, new TokenValidationParameters
            {
                ValidAudience = "data-crush.com",
                ValidIssuer = "data-crush.com",
                IssuerSigningKey = new SymmetricSecurityKey(key)
            }, out SecurityToken oldRefreshToken);
            var user = await _mongoContext.Users.Find(Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(u => u.userName, principal.Identity.Name)
              )
            ).FirstOrDefaultAsync();

            if (oldRefreshToken.ValidTo > DateTime.UtcNow)
            {
                List<Claim> claims = await BuildClaims(user);
                SecurityTokenDescriptor tokenDescriptor = new SecurityTokenDescriptor
                {
                    Issuer = "data-crush.com",
                    Audience = "data-crush.com",
                    Subject = new ClaimsIdentity(claims.ToArray()),
                    Expires = DateTime.UtcNow.AddMinutes(10),
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };
                var newToken = _tokenHandler.CreateToken(tokenDescriptor);
                user.token = _tokenHandler.WriteToken(newToken);
                user.tokenExpires = tokenDescriptor.Expires.GetValueOrDefault();
                SecurityTokenDescriptor refreshDescriptor = new SecurityTokenDescriptor
                {
                    Issuer = "data-crush.com",
                    Audience = "data-crush.com",
                    Subject = new ClaimsIdentity(claims.ToArray()),
                    Expires = DateTime.UtcNow.AddDays(30),
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };
                var newRefreshToken = _tokenHandler.CreateToken(refreshDescriptor);
                user.refreshToken = _tokenHandler.WriteToken(newRefreshToken);

            }
            else return null;
            return user.WithoutPassword();
        }
    }
}