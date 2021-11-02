namespace KCAA.Models
{
    public abstract class GameAction
    {
        public abstract bool IsActive { get; }

        public abstract void Method();
    }
}
