using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TypiQLDebug.Models.Mongo;
using TypiQLDebug.Models.Mongo.Types;
using TypiQLDebug.Services;

namespace TypiQLDebug.Models
{
    public class TestData
    {
        public UserService _userService;
        public MongoContext _mongoContext;
        public IHttpContextAccessor _accessor;
        public TestData(UserService userService, MongoContext mongoContext, IHttpContextAccessor accessor)
        {
            _userService = userService;
            _mongoContext = mongoContext;
            _accessor = accessor;
        }

        public async Task<User> Register(User user)
        {
            User createdUser = await _userService.Register(user);
            CreateCookies(createdUser);
            return createdUser;
        }
        public async Task<User> Authenticate(string username, string password)
        {
            User user = await _userService.Authenticate(username, password);
            CreateCookies(user);
            return user;
        }
        public async Task<User> Refresh()
        {
            if (!_accessor.HttpContext.Request.Cookies.ContainsKey("refreshToken")) throw new UnauthorizedAccessException();
            if (!_accessor.HttpContext.Request.Cookies.ContainsKey("accessToken")) throw new UnauthorizedAccessException();
            _accessor.HttpContext.Request.Cookies.TryGetValue("refreshToken", out string refreshToken);
            _accessor.HttpContext.Request.Cookies.TryGetValue("accessToken", out string accessToken);
            User user = await _userService.Refresh(accessToken, refreshToken);
            CreateCookies(user);
            return user;
        }
        public void Logout()
        {
            RemoveCookies();
        }
        public void CreateCookies(User user)
        {
            CookieOptions cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Expires = DateTimeOffset.Now.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            };
            _accessor.HttpContext.Response.Cookies.Append("accessToken", user.token, cookieOptions);
            _accessor.HttpContext.Response.Cookies.Append("refreshToken", user.refreshToken, cookieOptions);
        }
        public void RemoveCookies()
        {
            CookieOptions cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Expires = System.DateTimeOffset.Now.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            };
            _accessor.HttpContext.Response.Cookies.Delete("accessToken", cookieOptions);
            _accessor.HttpContext.Response.Cookies.Delete("refreshToken", cookieOptions);
        }
        public UpdateDefinition<T> BuildUpdate<T>(Dictionary<string, dynamic> updates)
        {
            List<UpdateDefinition<T>> updateSet = new List<UpdateDefinition<T>>();
            foreach (KeyValuePair<string, dynamic> kv in updates)
            {
                updateSet.Add(Builders<T>.Update.Set(kv.Key, kv.Value));
            }
            return Builders<T>.Update.Combine(updateSet);
        }
        public async Task<User> UpdateAccount(string username, string password, Dictionary<string, dynamic> update)
        {
            await _mongoContext.Users.UpdateOneAsync(Builders<User>.Filter.Eq(u => u.email, username), BuildUpdate<User>(update));
            User user = await _userService.Authenticate(username, password);
            CreateCookies(user);
            return user;
        }
        public async Task<User> UpdateAccount(string username, Dictionary<string, dynamic> update)
        {
            await _mongoContext.Users.UpdateOneAsync(Builders<User>.Filter.Eq(u => u.userName, username), BuildUpdate<User>(update));
            return await _mongoContext.Users.Find(u => u.userName == username).FirstOrDefaultAsync();
        }
        public async Task<User> UpdatePassword(string username, string oldPassword, string newPassword)
        {
            User user = await _userService.UpdatePassword(username, oldPassword, newPassword);
            CreateCookies(user);
            return user;
        }

        public async Task<User> GetUser(string username)
        {
            User user = await _mongoContext.Users.Find(Builders<User>.Filter.Eq(s => s.userName, username)).FirstOrDefaultAsync();
            return user.WithoutPassword();
        }
    }
}
