namespace KCAA.Settings
{
    public class MongoDBSettings
    {
        public static string ConfigKey => "MongoDBSettings";

        public string ConnectionString { get; set; }

        public string DatabaseName { get; set; }
    }
}
