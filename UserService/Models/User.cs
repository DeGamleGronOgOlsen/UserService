using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace UserService.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)] // Eller BsonType.Binary hvis du vil gemme det binært
        public Guid Id { get; set; }
        public string? Username { get; set; } // Brugernavn
        public string? Password { get; set; } // Adgangskode
        public Role? Role { get; set; } // Rolle (f.eks. "admin" eller "user")
        public string? Name { get; set; }
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
        public short PostalCode { get; set; }
        public string? City { get; set; }
        public string? EmailAddress { get; set; }
        public string? PhoneNumber { get; set; }
    }

    public enum Role
    {
        Admin,
        User,
        Customer
    }

}


