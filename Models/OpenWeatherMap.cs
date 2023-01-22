
using System.Collections.Generic;

namespace Models
{
    public sealed class Coord
    {
        public double lon { get; set; }

        public double lat { get; set; }
    }

    public sealed class Weather
    {
        public int id { get; set; }

        public string main { get; set; }

        public string description { get; set; }

        public string icon { get; set; }
    }

    public sealed class Wind
    {
        public double speed { get; set; }

        public int deg { get; set; }
    }

    public sealed class Clouds
    {
        public int all { get; set; }
    }

    public sealed class Sys
    {
        public int type { get; set; }

        public int id { get; set; }

        public string country { get; set; }

        public int sunrise { get; set; }

        public int sunset { get; set; }
    }

    public sealed class WeatherInfo
    {
        public Coord coord { get; set; }

        public IList<Weather> weather { get; set; }

        public string Base { get; set; }

        public Main main { get; set; }

        public Wind wind { get; set; }

        public Clouds clouds { get; set; }

        public int dt { get; set; }

        public Sys sys { get; set; }

        public int timezone { get; set; }

        public int id { get; set; }

        public string name { get; set; }

        public int cod { get; set; }
    }

    public sealed class Main
    {
        public double temp { get; set; }

        public double feels_like { get; set; }

        public double temp_min { get; set; }

        public double temp_max { get; set; }

        public int pressure { get; set; }

        public int sea_level { get; set; }

        public int grnd_level { get; set; }

        public int humidity { get; set; }

        public double temp_kf { get; set; }
    }

    public sealed class Sys2
    {
        public string pod { get; set; }
    }

    public sealed class Rain
    {
        public double three_hour { get; set; }
    }

    public sealed class List
    {
        public int dt { get; set; }

        public Main main { get; set; }

        public List<Weather> weather { get; set; }

        public Clouds clouds { get; set; }

        public Wind wind { get; set; }

        public int visibility { get; set; }

        public double pop { get; set; }

        public Sys2 sys { get; set; }

        public string dt_txt { get; set; }

        public Rain rain { get; set; }
    }

    public sealed class City
    {
        public int id { get; set; }

        public string name { get; set; }

        public Coord coord { get; set; }

        public string country { get; set; }

        public int population { get; set; }

        public int timezone { get; set; }

        public int sunrise { get; set; }

        public int sunset { get; set; }
    }

    public sealed class Forecast
    {
        public string cod { get; set; }

        public int message { get; set; }

        public int cnt { get; set; }

        public List<List> list { get; set; }

        public City city { get; set; }
    }
}
