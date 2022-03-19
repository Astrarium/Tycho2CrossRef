using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Tycho2CrossRef
{
    class Star
    {
        public Star(string raw)
        {
            Raw = raw;
            if (!IsEmpty)
            {
                HR = ushort.Parse(raw.Substring(0, 4).Trim());
                string hdNumber = raw.Substring(25, 6).Trim();
                HD = int.Parse(hdNumber);
                Mag = Convert.ToSingle(raw.Substring(102, 5), CultureInfo.InvariantCulture);
            }
        }
        public int HR { get; init; }
        public int HD { get; init; }
        public float Mag { get; set; }
        public string Raw { get; set; }
        public bool IsEmpty => Raw[94] == ' ';
    }

    class Tycho2Star
    {
        public short Tyc1 { get; set; }
        public short Tyc2 { get; set; }
        public char Tyc3 { get; set; }
        public float Mag { get; set; }
    }

    class Tycho2Region
    {
        public long FirstStarId { get; set; }
        public long LastStarId { get; set; }
    }

    class Program
    {
        const int CATALOG_RECORD_LEN = 33;

        static List<Star> stars = new List<Star>();
        static Dictionary<int, string> HD_Tyc2 = new Dictionary<int, string>();
        static ICollection<Tycho2Region> IndexRegions = new List<Tycho2Region>();
        static BinaryReader CatalogReader;

        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Please specify path to Tycho2 catalog directory as command line parameter.");
                Environment.Exit(-1);
            }

            string tycho2dir = args[0];
            if (!Directory.Exists(tycho2dir))
            {
                Console.WriteLine("Specified directory does not exist.");
                Environment.Exit(-1);
            }

            if (!File.Exists(Path.Combine(tycho2dir, "tycho2.dat")) || !File.Exists(Path.Combine(tycho2dir, "tycho2.idx")))
            {
                Console.WriteLine("The directory should contain both tycho2.dat and tycho2.idx files.");
                Environment.Exit(-1);
            }

            LoadTycho2(tycho2dir);
            LoadHdTyc2();
            LoadBSC();
            WriteTyc2Bsc();
        }

        static void LoadHdTyc2()
        {
            string fileName = @"Data\tyc2_hd.dat";
            int count = 0;

            using (var reader = new StreamReader(fileName))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string tyc2 = line.Substring(0, 12);
                    int hd = int.Parse(line.Substring(14, 6));

                    if (!HD_Tyc2.ContainsKey(hd))
                    {
                        HD_Tyc2.Add(hd, tyc2);
                    }
                    else
                    {
                        count++;
                    }
                }
            }

            Console.WriteLine($"HD-Tyc2 cross reference loaded, duplicate records count: {count}");
        }

        static void LoadBSC()
        {
            string fileName = @"Data\bsc.dat";

            using (var reader = new StreamReader(fileName))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    stars.Add(new Star(line));
                }
            }

            Console.WriteLine($"BSC loaded, stars count: {stars.Count}");
        }

        static void LoadTycho2(string dataPath)
        {
            try
            {
                string indexFile = Path.Combine(dataPath, "tycho2.idx");
                string catalogFile = Path.Combine(dataPath, "tycho2.dat");
                StreamReader sr = new StreamReader(indexFile);
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    string[] chunks = line.Split(';');
                    IndexRegions.Add(new Tycho2Region()
                    {
                        FirstStarId = Convert.ToInt64(chunks[0].Trim()),
                        LastStarId = Convert.ToInt64(chunks[1].Trim())
                    });
                }

                sr.Close();

                // Open Tycho2 catalog file
                CatalogReader = new BinaryReader(File.Open(catalogFile, FileMode.Open, FileAccess.Read));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to initialize Tycho2 calculator: {ex}");
            }
        }

        static Tycho2Star SearchStar(int tyc1, short tyc2, char tyc3)
        {
            Tycho2Region region = IndexRegions.ElementAt(tyc1 - 1);
            return GetStarsInRegion(region, tyc2, tyc3).FirstOrDefault(s => s.Tyc1 == tyc1 && s.Tyc2 == tyc2 && s.Tyc3 == tyc3);
        }

        static ICollection<Tycho2Star> GetStarsInRegion(Tycho2Region region, short? tyc2, char tyc3)
        {
            // seek reading position 
            CatalogReader.BaseStream.Seek(CATALOG_RECORD_LEN * (region.FirstStarId - 1), SeekOrigin.Begin);

            // count of records in current region
            int count = (int)(region.LastStarId - region.FirstStarId);

            // read region in memory for fast access
            byte[] buffer = CatalogReader.ReadBytes(CATALOG_RECORD_LEN * count);

            var stars = new List<Tycho2Star>();

            for (int i = 0; i < count && stars.Count < 50; i++)
            {
                Tycho2Star star = GetStar(buffer, i * CATALOG_RECORD_LEN, tyc2, tyc3);
                if (star != null)
                {
                    stars.Add(star);
                }
            }

            return stars;
        }

        static Tycho2Star GetStar(byte[] buffer, int offset, short? tyc2, char tyc3)
        {
            short t2 = BitConverter.ToInt16(buffer, offset + 2);
            char t3 = (char)buffer[offset + 4];

            if ((tyc2 == null || tyc2.Value == t2) && (tyc3 == t3))
            {
                return ReadTyc2Star(buffer, offset);
            }
            else
            {
                return null;
            }
        }

        static Tycho2Star ReadTyc2Star(byte[] buffer, int offset)
        {
            Tycho2Star star = new Tycho2Star();
            star.Tyc1 = BitConverter.ToInt16(buffer, offset);
            star.Tyc2 = BitConverter.ToInt16(buffer, offset + 2);
            star.Tyc3 = (char)buffer[offset + 4];
            star.Mag = BitConverter.ToSingle(buffer, offset + 29);
            return star;
        }

        static void WriteTyc2Bsc()
        {
            int notFoundStars = 0;
            int magDiffStars = 0;
            int crossRefsCount = 0;
            using (var crossRefFile = new StreamWriter("CrossRef.txt"))
            using (var correctedBscFile = new StreamWriter("Stars.dat"))
            {
                foreach (var star in stars)
                {
                    if (!star.IsEmpty)
                    { 
                        string tychoIdentifier = HD_Tyc2[star.HD];

                        string[] chunks = tychoIdentifier.Split(' ', StringSplitOptions.TrimEntries);

                        Tycho2Star tychoStar = SearchStar(int.Parse(chunks[0]), short.Parse(chunks[1]), chunks[2][0]);
                        if (tychoStar != null)
                        {
                            if (Math.Abs(Math.Round(tychoStar.Mag, 2) - star.Mag) >= 0.01)
                            {
                                // change the raw string: correct magnitude according to match the Tycho2 catalogue
                                star.Raw = star.Raw.Substring(0, 102) + Math.Round(tychoStar.Mag, 2).ToString(CultureInfo.InvariantCulture).PadLeft(5) + star.Raw.Substring(107);
                                magDiffStars++;
                            }

                            crossRefFile.WriteLine($"{star.HR} {tychoStar.Tyc1}-{tychoStar.Tyc2}-{tychoStar.Tyc3}");
                            crossRefsCount++;
                        }
                        else
                        {
                            notFoundStars++;
                        }
                    }

                    correctedBscFile.WriteLine(star.Raw);
                }
            }

            Console.WriteLine($"Count of stars not found in Tycho2 catalogue: {notFoundStars}");
            Console.WriteLine($"Count of stars with magnitude difference: {magDiffStars}");
            Console.WriteLine($"Count of cross-referenced stars: {crossRefsCount}");
        }
    }
}
