using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace TypiQLDebug.Models.Mongo.Types
{
  public class User
  {
    [BsonId]
    public ObjectId _id { get; set; }
    [BsonElement("firstName")]
    public string firstName { get; set; }
    [BsonElement("surname")]
    public string surname { get; set; }
    [BsonElement("userName")]
    public string userName { get; set; }
    public string token { get; set; }
    public DateTime tokenExpires { get; set; }
    public string refreshToken { get; set; }
    [BsonElement("salt")]
    public byte[] salt { get; set; }
    [BsonElement("password")]
    public string password { get; set; }
    public string displayName { get { return $"{firstName} {surname}"; } }
    [BsonElement("email")]
    public string email { get; set; }
    [BsonElement("admin")]
    public bool admin { get; set; }

    public User WithoutPassword()
    {
      User user = this;
      user.password = null;
      return user;
    }
  }
}