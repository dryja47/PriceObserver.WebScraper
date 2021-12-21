namespace PriceObserver.WebScraper
{
    public class Proxy
    {
        public string Address { get; set; }
        public string Port { get; set; }
        public string Code { get; set; }
        public string Country { get; set; }
        public AnonymityLevels Anonymity { get; set; } = 0;
        public bool Https { get; set; }
        
        public enum AnonymityLevels
        {
            Transparent = 0,
            Anonymous = 1,
            Elite = 2
        }
    }

}