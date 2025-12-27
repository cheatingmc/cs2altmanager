namespace CacheLoginToolWPF.Models
{
    public class Account
    {
        public string Username { get; set; } = string.Empty;
        public string SteamId { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public bool IsPrime { get; set; } = false;
        public string ProfilePictureUrl { get; set; } = string.Empty;
    }
}

