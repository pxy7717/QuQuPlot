using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Data;
using ExcelDataReader;
using QuquPlot.Models;
using System.Windows;

namespace QuquPlot.Utils
{
    public static class FileUtils
    {
        /// <summary>
        /// 获取智能后缀，用于区分同名文件
        /// </summary>
        /// <param name="newFileName">新文件名</param>
        /// <param name="existingFiles">已存在的文件列表</param>
        /// <returns>智能后缀字符串</returns>
        public static string GetSmartSuffix(string? newFileName, List<string> existingFiles)
        {
            if (string.IsNullOrEmpty(newFileName) || existingFiles == null || existingFiles.Count == 0)
                return string.Empty;

            string[] newParts = Path.GetFileNameWithoutExtension(newFileName).Split('_');
            var existingPartsList = existingFiles
                .Select(f => Path.GetFileNameWithoutExtension(f).Split('_'))
                .ToList();

            // 从后往前比较，找到第一个不同的后缀
            for (int i = newParts.Length - 1; i >= 0; i--)
            {
                string currentSuffix = string.Join("_", newParts.Skip(i));
                bool suffixExists = existingPartsList.Any(parts => 
                    parts.Length > i && string.Join("_", parts.Skip(i)) == currentSuffix);
                
                if (!suffixExists)
                {
                    return currentSuffix;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 检测文件分隔符
        /// </summary>
        /// <param name="lines">文件行数组</param>
        /// <returns>检测到的分隔符</returns>
        public static string? DetectDelimiter(string[] lines)
        {
            if (lines == null || lines.Length == 0) return null;

            var delimiters = new[] { ",", "\t", ";", "|", " " };
            var delimiterScores = new Dictionary<string, int>();

            foreach (var delimiter in delimiters)
            {
                delimiterScores[delimiter] = 0;
            }

            // 分析前几行来确定分隔符
            int linesToAnalyze = Math.Min(10, lines.Length);
            for (int i = 0; i < linesToAnalyze; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                foreach (var delimiter in delimiters)
                {
                    var parts = lines[i].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        delimiterScores[delimiter]++;
                    }
                }
            }

            // 返回得分最高的分隔符
            return delimiterScores.OrderByDescending(x => x.Value).FirstOrDefault().Key;
        }

        /// <summary>
        /// 查找数据范围
        /// </summary>
        /// <param name="lines">文件行数组</param>
        /// <param name="logAction">日志记录委托</param>
        /// <returns>(第一个数据行索引, 最后一个数据行索引)</returns>
        public static (int firstDataIndex, int lastDataIndex) FindDataRange(string[] lines, Action<string>? logAction = null)
        {
            logAction?.Invoke($"开始检测数据范围，文件行数: {lines.Length}");
            var delimiter = DetectDelimiter(lines);
            if (delimiter == null)
            {
                logAction?.Invoke("未检测到分隔符，尝试按空格分割");
                delimiter = " ";
            }

            int firstDataIndex = -1;
            int lastDataIndex = -1;

            // 查找第一个数据行
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                string[] parts = lines[i].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && parts.All(p => double.TryParse(p, out _)))
                {
                    firstDataIndex = i;
                    break;
                }
            }

            if (firstDataIndex == -1)
            {
                logAction?.Invoke("未找到数据行");
                return (-1, -1);
            }

            // 查找最后一个数据行
            for (int i = lines.Length - 1; i >= firstDataIndex; i--)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                string[] parts = lines[i].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && parts.All(p => double.TryParse(p, out _)))
                {
                    lastDataIndex = i;
                    break;
                }
            }

            logAction?.Invoke($"数据范围: 第{firstDataIndex + 1}行到第{lastDataIndex + 1}行");
            return (firstDataIndex, lastDataIndex);
        }

        /// <summary>
        /// 通用数据区提取：跳过注释、空行、区块头，自动识别表头和数据
        /// </summary>
        public static List<string> ExtractDataBlock(List<string> lines, Action<string>? logAction = null)
        {
            var cleaned = new List<string>();
            bool inDataBlock = false;
            bool headerFound = false;
            bool hasBlockHeader = lines.Any(l => l.Trim().StartsWith("BEGIN") || l.Contains("_DATA"));

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("!") || line.StartsWith("#")) continue;

                // 进入数据区（仅当有区块头时才用此逻辑）
                if (hasBlockHeader)
                {
                    if (!inDataBlock && (line.StartsWith("BEGIN") || line.Contains("_DATA")))
                    {
                        inDataBlock = true;
                        continue;
                    }
                    if (!inDataBlock) continue;
                    if (line.StartsWith("END")) break;
                }

                // 只保留第一个表头和后续数据
                if (!headerFound)
                {
                    cleaned.Add(line);
                    headerFound = true;
                    continue;
                }

                // 判断是否为数据行（全为数字或数字+分隔符）
                var parts = line.Split(new[] { ',', '\t', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.All(p => double.TryParse(p, out _)))
                {
                    cleaned.Add(line);
                }
                else
                {
                    // 如果遇到新表头，可能是下一个区块，直接跳出
                    if (hasBlockHeader)
                        break;
                }
            }

            logAction?.Invoke($"提取到 {cleaned.Count} 行有效数据（含表头）");
            return cleaned;
        }

        /// <summary>
        /// 处理文本文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="logAction">日志记录委托</param>
        /// <returns>处理后的行列表</returns>
        public static List<string> ProcessTxtFile(string filePath, Action<string>? logAction = null)
        {
            logAction?.Invoke($"开始处理文本文件: {Path.GetFileName(filePath)}");
            try
            {
                var lines = File.ReadAllLines(filePath).ToList();
                logAction?.Invoke($"读取到 {lines.Count} 行数据");
                // 检测分隔符
                var delimiter = DetectDelimiter(lines.ToArray());
                if (delimiter == null)
                {
                    delimiter = " ";
                }
                var processedLines = new List<string>();
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                    processedLines.Add(string.Join(",", parts));
                }
                var cleaned = ExtractDataBlock(processedLines, logAction);
                return cleaned;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"处理文本文件时出错: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 处理Excel文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="logAction">日志记录委托</param>
        /// <returns>处理后的行列表</returns>
        public static List<string> ProcessExcelFile(string filePath, Action<string>? logAction = null)
        {
            logAction?.Invoke($"开始处理Excel文件: {Path.GetFileName(filePath)}");
            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read);
                using var reader = ExcelReaderFactory.CreateReader(stream);
                var result = reader.AsDataSet();
                var table = result.Tables[0];
                var lines = new List<string>();
                for (int row = 0; row < table.Rows.Count; row++)
                {
                    var rowData = new List<string>();
                    for (int col = 0; col < table.Columns.Count; col++)
                    {
                        var value = table.Rows[row][col];
                        rowData.Add(value?.ToString() ?? "");
                    }
                    lines.Add(string.Join(",", rowData));
                }
                logAction?.Invoke($"处理完成，有效行数: {lines.Count}");
                var cleaned = ExtractDataBlock(lines, logAction);
                return cleaned;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"处理Excel文件时出错: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 处理CSV文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="logAction">日志记录委托</param>
        /// <returns>处理后的行列表</returns>
        public static List<string> ProcessCsvFile(string filePath, Action<string>? logAction = null)
        {
            logAction?.Invoke($"开始处理CSV文件: {Path.GetFileName(filePath)}");
            try
            {
                var lines = File.ReadAllLines(filePath).ToList();
                logAction?.Invoke($"读取到 {lines.Count} 行数据");
                var cleaned = ExtractDataBlock(lines, logAction);
                return cleaned;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"处理CSV文件时出错: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 处理数据行并生成曲线数据
        /// </summary>
        /// <param name="lines">数据行列表</param>
        /// <param name="sourceFileName">源文件名</param>
        /// <param name="logAction">日志记录委托</param>
        /// <param name="addCurveAction">添加曲线的委托</param>
        /// <param name="updateAxisLabelsAction">更新坐标轴标签的委托</param>
        /// <returns>处理结果</returns>
        public static bool ProcessDataLines(
            List<string> lines, 
            string sourceFileName,
            Action<string>? logAction = null,
            Func<string, double[], double[], string, bool, object?>? addCurveAction = null,
            Action<string, string>? updateAxisLabelsAction = null)
        {
            if (lines.Count == 0)
            {
                logAction?.Invoke("没有数据需要处理");
                return false;
            }

            var delimiter = ","; // 使用逗号作为标准分隔符，因为数据已经被预处理为标准格式
            var (firstDataIndex, lastDataIndex) = FindDataRange(lines.ToArray(), logAction);

            if (firstDataIndex >= lastDataIndex)
            {
                logAction?.Invoke($"未找到有效数据");
                return false;
            }

            var data = new List<double[]>();
            string[]? headers = null;

            // 检查第一行是否为表头
            if (firstDataIndex > 0)
            {
                string[] potentialHeader = lines[firstDataIndex - 1].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                if (potentialHeader.All(p => !double.TryParse(p, out _)))
                {
                    headers = potentialHeader;
                    if (headers.Length >= 2 && updateAxisLabelsAction != null)
                    {
                        updateAxisLabelsAction(headers[0], headers[1]);
                    }
                }
            }

            // 处理有效数据范围
            for (int i = firstDataIndex; i <= lastDataIndex; i++)
            {
                string[] parts = lines[i].Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 1) continue;
                var row = new double[parts.Length];
                bool valid = true;
                for (int j = 0; j < parts.Length; j++)
                {
                    if (!double.TryParse(parts[j], out row[j]))
                    {
                        valid = false;
                        break;
                    }
                }
                if (valid)
                    data.Add(row);
            }

            if (data.Count == 0) return false;

            int colCount = data[0].Length;
            int rowCount = data.Count;

            // 单列数据处理
            if (colCount == 1)
            {
                double[] ys = new double[rowCount];
                for (int row = 0; row < rowCount; row++)
                    ys[row] = data[row][0];
                double[] xs = Enumerable.Range(0, rowCount).Select(i => (double)i).ToArray();
                logAction?.Invoke($"检测到单列数据，使用索引作为X轴，数量={rowCount}");
                var label = headers != null && headers.Length > 0 && !string.IsNullOrEmpty(headers[0]) ? headers[0] : "Y";
                addCurveAction?.Invoke(label, xs, ys, sourceFileName, true);
                return true;
            }

            // 检查是否所有列都是数值
            bool allColumnsAreNumeric = true;
            for (int col = 0; col < colCount; col++)
            {
                bool columnIsNumeric = true;
                for (int row = 0; row < rowCount; row++)
                {
                    if (!double.TryParse(data[row][col].ToString(), out _))
                    {
                        columnIsNumeric = false;
                        break;
                    }
                }
                if (!columnIsNumeric)
                {
                    allColumnsAreNumeric = false;
                    break;
                }
            }

            // 如果所有列都是数值，使用第一列作为X轴
            if (allColumnsAreNumeric)
            {
                // 提取X值（第一列）
                double[] xs = new double[rowCount];
                for (int row = 0; row < rowCount; row++)
                {
                    xs[row] = data[row][0];
                }
                logAction?.Invoke($"X轴范围: {xs[0]} 到 {xs[xs.Length-1]}");

                // 处理Y值（其他列）
                bool shouldLimitVisibility = colCount > 6;
                for (int col = 1; col < colCount; col++)
                {
                    double[] ys = new double[rowCount];
                    for (int row = 0; row < rowCount; row++)
                    {
                        ys[row] = data[row][col];
                    }
                    string label = headers != null && headers.Length > col ? headers[col] : $"列{col+1}";
                    addCurveAction?.Invoke(label, xs, ys, sourceFileName, !shouldLimitVisibility || col == 1);
                }
            }
            else
            {
                // 如果有非数值列，将所有列都作为Y值，使用索引作为X轴
                logAction?.Invoke("检测到非数值列，所有列将作为Y值，使用索引作为X轴");
                double[] xs = Enumerable.Range(0, rowCount).Select(i => (double)i).ToArray();
                
                for (int col = 0; col < colCount; col++)
                {
                    double[] ys = new double[rowCount];
                    bool columnIsValid = true;
                    
                    for (int row = 0; row < rowCount; row++)
                    {
                        if (!double.TryParse(data[row][col].ToString(), out ys[row]))
                        {
                            columnIsValid = false;
                            break;
                        }
                    }
                    
                    if (columnIsValid)
                    {
                        string label = headers != null && headers.Length > col ? headers[col] : $"列{col+1}";
                        addCurveAction?.Invoke(label, xs, ys, sourceFileName, true);
                    }
                }
            }

            logAction?.Invoke("数据处理完成");
            return true;
        }

        /// <summary>
        /// 获取文件扩展名
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件扩展名（小写）</returns>
        public static string GetFileExtension(string filePath)
        {
            return Path.GetExtension(filePath).ToLower();
        }

        /// <summary>
        /// 检查文件是否可读
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>是否可读</returns>
        public static bool IsFileReadable(string filePath)
        {
            try
            {
                return File.Exists(filePath) && File.Exists(filePath);
            }
            catch
            {
                return false;
            }
        }
    }
} 