namespace KCAA.Settings
{
    public class MongoDBSettings
    {
        public const string ConfigKey = "MongoDBSettings";

        public string ConnectionString { get; set; }

        public string DatabaseName { get; set; }
    }
}
