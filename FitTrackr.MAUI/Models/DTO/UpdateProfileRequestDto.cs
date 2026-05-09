namespace FitTrackr.MAUI.Models.DTO
{
    public class UpdateProfileRequestDto
    {
        public string Username { get; set; } = string.Empty;
        public int? HeightCm { get; set; }
        public int? WeightKg { get; set; }
        public string? Gender { get; set; }
        public string? Goal { get; set; }
    }
}
