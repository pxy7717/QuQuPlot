using System.IO;

// using QuquPlot.Utils;

namespace QuquPlot.Models
{
    // Complex number structure for S-parameter calculations
    public struct Complex
    {
        public double Real;
        public double Imag;

        public Complex(double real, double imag)
        {
            Real = real;
            Imag = imag;
        }

        public static Complex FromDBAndAngle(double db, double angleDegrees)
        {
            double magLinear = Math.Pow(10, db / 20);
            double angleRad = angleDegrees * Math.PI / 180;
            return new Complex(
                magLinear * Math.Cos(angleRad),
                magLinear * Math.Sin(angleRad)
            );
        }

        public static Complex operator -(Complex a, Complex b)
        {
            return new Complex(a.Real - b.Real, a.Imag - b.Imag);
        }

        public static Complex operator +(Complex a, Complex b)
        {
            return new Complex(a.Real + b.Real, a.Imag + b.Imag);
        }

        public static Complex operator *(double scalar, Complex c)
        {
            return new Complex(scalar * c.Real, scalar * c.Imag);
        }

        public double Magnitude()
        {
            return Math.Sqrt(Real * Real + Imag * Imag);
        }

        public double MagnitudeDB()
        {
            return 20 * Math.Log10(Magnitude());
        }
    }

    public class SParameterData
    {
        public List<double> Frequencies { get; set; } = new List<double>();
        public Dictionary<string, List<double>> Magnitudes { get; set; } = new Dictionary<string, List<double>>();
        public Dictionary<string, int> CurveLengths { get; set; } = new Dictionary<string, int>();
        public string? Format { get; set; }
        public string? Reference { get; set; }
        public bool IsCSV { get; set; }

        // Mapping for CSV column names to S-parameters
        public static readonly Dictionary<string, string> CsvToSParameterMap = new Dictionary<string, string>
        {
            { "S22 Log Mag(dB)", "S22" },
            { "Sdd11 Log Mag(dB)", "S11" },
            { "Ssd21 Log Mag(dB)", "Ssd21" }
        };
    }

    public static class SParameterFileParser
    {
        public static SParameterData ParseFile(string filePath)
        {
            var result = new SParameterData();
            
            try
            {
                var lines = File.ReadAllLines(filePath);

                // Check if it's a CSV file
                result.IsCSV = filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

                if (result.IsCSV)
                {
                    ParseCSVFile(lines, result);
                }
                else if (filePath.EndsWith(".s3p", StringComparison.OrdinalIgnoreCase))
                {
                    ParseS3pFile(lines, result);
                }
                else
                {
                    ParseSParameterFile(lines, result);
                }

                // Set the length for each curve
                foreach (var curve in result.Magnitudes.Keys)
                {
                    result.CurveLengths[curve] = result.Magnitudes[curve].Count;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error parsing file: {ex.Message}", ex);
            }

            return result;
        }

        private static void ParseCSVFile(string[] lines, SParameterData result)
        {
            // Skip header lines until we find the data
            int startIndex = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("Freq(Hz)"))
                {
                    startIndex = i + 1;
                    break;
                }
            }

            // Initialize magnitude lists for CSV parameters
            foreach (var mapping in SParameterData.CsvToSParameterMap.Values)
            {
                result.Magnitudes[mapping] = new List<double>();
            }

            // Process data lines
            for (int i = startIndex; i < lines.Length; i++)
            {
                var values = lines[i].Split(',');
                if (values.Length >= 4)
                {
                    // Convert frequency to GHz
                    double freqGHz = double.Parse(values[0]) / 1e9;
                    result.Frequencies.Add(freqGHz);

                    // Add magnitudes directly (they're already in dB)
                    result.Magnitudes["S22"].Add(double.Parse(values[1]));
                    result.Magnitudes["S11"].Add(double.Parse(values[2]));
                    result.Magnitudes["Ssd21"].Add(double.Parse(values[3]));
                }
            }
        }

        private static void ParseSParameterFile(string[] lines, SParameterData result)
        {
            var dataLines = new List<string>();
            string? formatLine = null;

            // First pass: find format line and collect data lines
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                    continue;

                if (trimmedLine.StartsWith("#"))
                {
                    formatLine = trimmedLine;
                    continue;
                }

                if (!trimmedLine.StartsWith("!") && !trimmedLine.StartsWith("#"))
                {
                    dataLines.Add(trimmedLine);
                }
            }

            // Parse format line if present
            if (formatLine != null)
            {
                var formatParts = formatLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (formatParts.Length >= 4)
                {
                    result.Format = formatParts[2]; // S
                    result.Reference = formatParts[3]; // dB
                }
            }

            // Initialize magnitude lists for required parameters
            result.Magnitudes["S11"] = new List<double>();
            result.Magnitudes["S22"] = new List<double>();
            result.Magnitudes["S33"] = new List<double>();
            result.Magnitudes["S44"] = new List<double>();
            result.Magnitudes["Ssd21"] = new List<double>();
            result.Magnitudes["Sdd11"] = new List<double>();

            // Process data in groups of 4 lines for S4P files
            for (int i = 0; i + 3 < dataLines.Count; i += 4)
            {
                var freqRow = dataLines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var s2Row = dataLines[i + 1].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var s3Row = dataLines[i + 2].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var s4Row = dataLines[i + 3].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (freqRow.Length > 0)
                {
                    // Convert frequency to GHz
                    double freqGHz = double.Parse(freqRow[0]) / 1e9;
                    result.Frequencies.Add(freqGHz);

                    // Process reflection parameters
                    ProcessReflectionParameters(freqRow, s2Row, s3Row, s4Row, result.Magnitudes);
                    // Calculate differential signals
                    CalculateDifferentialSignals(s2Row, s3Row, s4Row, result.Magnitudes);
                }
            }
        }

        private static void ProcessReflectionParameters(string[] s1Row, string[] s2Row, string[] s3Row, string[] s4Row, 
            Dictionary<string, List<double>> magnitudes)
        {
            // Process S11
            if (s1Row.Length >= 4)
            {
                double s11_db = double.Parse(s1Row[0]);
                double s11_ang = double.Parse(s1Row[1]);
                var s11 = Complex.FromDBAndAngle(s11_db, s11_ang);
                magnitudes["S11"].Add(s11.MagnitudeDB());
            }

            // Process S22
            if (s2Row.Length >= 4)
            {
                double s22_db = double.Parse(s2Row[2]);
                double s22_ang = double.Parse(s2Row[3]);
                var s22 = Complex.FromDBAndAngle(s22_db, s22_ang);
                magnitudes["S22"].Add(s22.MagnitudeDB());
            }

            // Process S33
            if (s3Row.Length >= 4)
            {
                double s33_db = double.Parse(s3Row[4]);
                double s33_ang = double.Parse(s3Row[5]);
                var s33 = Complex.FromDBAndAngle(s33_db, s33_ang);
                magnitudes["S33"].Add(s33.MagnitudeDB());
            }

            // Process S44
            if (s4Row.Length >= 4)
            {
                double s44_db = double.Parse(s4Row[6]);
                double s44_ang = double.Parse(s4Row[7]);
                var s44 = Complex.FromDBAndAngle(s44_db, s44_ang);
                magnitudes["S44"].Add(s44.MagnitudeDB());
            }
        }

        private static void CalculateDifferentialSignals(string[] s2Row, string[] s3Row, string[] s4Row, Dictionary<string, List<double>> magnitudes)
        {
            if (s2Row.Length < 8 || s3Row.Length < 8 || s4Row.Length < 8) return;

            // Get S-parameters for SSD21 calculation
            double s42_db = double.Parse(s4Row[2]);
            double s42_ang = double.Parse(s4Row[3]);
            double s43_db = double.Parse(s4Row[4]);
            double s43_ang = double.Parse(s4Row[5]);

            if (magnitudes["Ssd21"].Count < 10)
            {


                // Convert to complex numbers
                var s42 = Complex.FromDBAndAngle(s42_db, s42_ang);
                var s43 = Complex.FromDBAndAngle(s43_db, s43_ang);



                // Calculate SSD21 = 0.5 * (S42 - S43)
                var ssd21 = 0.5 * (s42 - s43);


                // Store the value
                double ssd21_db = ssd21.MagnitudeDB();
                magnitudes["Ssd21"].Add(ssd21_db);

            }
            else
            {
                // Convert to complex numbers
                var s42 = Complex.FromDBAndAngle(s42_db, s42_ang);
                var s43 = Complex.FromDBAndAngle(s43_db, s43_ang);

                // Calculate SSD21 = 0.5 * (S42 - S43)
                var ssd21 = 0.5 * (s42 - s43);
                magnitudes["Ssd21"].Add(ssd21.MagnitudeDB());
            }

            // Get S-parameters for SDD11 calculation
            double s22_db = double.Parse(s2Row[2]);
            double s22_ang = double.Parse(s2Row[3]);
            double s23_db = double.Parse(s2Row[4]);
            double s23_ang = double.Parse(s2Row[5]);
            double s32_db = double.Parse(s3Row[2]);
            double s32_ang = double.Parse(s3Row[3]);
            double s33_db = double.Parse(s3Row[4]);
            double s33_ang = double.Parse(s3Row[5]);

            // Convert to complex numbers
            var s22 = Complex.FromDBAndAngle(s22_db, s22_ang);
            var s23 = Complex.FromDBAndAngle(s23_db, s23_ang);
            var s32 = Complex.FromDBAndAngle(s32_db, s32_ang);
            var s33 = Complex.FromDBAndAngle(s33_db, s33_ang);

            // Calculate SDD11 = 0.5 * (S22 - S23 - S32 + S33)
            var sdd11 = 0.5 * (s22 - s23 - s32 + s33);
            magnitudes["Sdd11"].Add(sdd11.MagnitudeDB());
        }

        // s3p专用解析
        private static void ParseS3pFile(string[] lines, SParameterData result)
        {
            var dataLines = new List<string>();
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                    continue;
                if (!trimmedLine.StartsWith("!") && !trimmedLine.StartsWith("#"))
                    dataLines.Add(trimmedLine);
            }
            result.Magnitudes["Ssd21"] = new List<double>();
            result.Magnitudes["Sdd11"] = new List<double>();
            result.Magnitudes["Sss22"] = new List<double>();

            for (int i = 0; i + 2 < dataLines.Count; i += 3)
            {
                var row1 = dataLines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var row2 = dataLines[i + 1].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var row3 = dataLines[i + 2].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (row1.Length >= 1 && row2.Length >= 6 && row3.Length >= 6)
                {
                    double freqGHz = double.Parse(row1[0]) / 1e9;
                    result.Frequencies.Add(freqGHz);


                    // Sdd11 
                    double mag_s11 = double.Parse(row1[1]);
                    double ang_s11 = double.Parse(row1[2]);
                    var sdd11 = new Complex(mag_s11 * Math.Cos(ang_s11 * Math.PI / 180), mag_s11 * Math.Sin(ang_s11 * Math.PI / 180));
                    double sdd11dB = 20 * Math.Log10(sdd11.Magnitude());
                    result.Magnitudes["Sdd11"].Add(sdd11dB);


                    // Ssd21
                    double mag_s21 = double.Parse(row3[0]);
                    double ang_s21 = double.Parse(row3[1]);
                    var ssd21 = new Complex(mag_s21 * Math.Cos(ang_s21 * Math.PI / 180), mag_s21 * Math.Sin(ang_s21 * Math.PI / 180));
                    double ssd21dB = 20 * Math.Log10(ssd21.Magnitude());
                    result.Magnitudes["Ssd21"].Add(ssd21dB);

                    // Sss22
                    double mag_s22 = double.Parse(row3[4]);
                    double ang_s22 = double.Parse(row3[5]);
                    var sss22 = new Complex(mag_s22 * Math.Cos(ang_s22 * Math.PI / 180), mag_s22 * Math.Sin(ang_s22 * Math.PI / 180));
                    double sss22dB = 20 * Math.Log10(sss22.Magnitude());
                    result.Magnitudes["Sss22"].Add(sss22dB);
                }
            }
        }
    }
} 