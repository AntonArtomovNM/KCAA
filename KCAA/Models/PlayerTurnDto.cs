using KCAA.Models.MongoDB;

namespace KCAA.Models
{
    public class PlayerTurnDto
    {
        public string PlayerId { get; set; }

        public Character Character { get; set; }
    }
}
