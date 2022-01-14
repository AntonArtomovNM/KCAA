using KCAA.Models.Characters;

namespace KCAA.Models
{
    public class PlayerTurnDto
    {
        public string PlayerId { get; set; }

        public CharacterBase Character { get; set; }
    }
}
