using System.Collections.Generic;
using System.IO;
using ETABSModelDefinitionValidator.EtabsApi;

namespace ETABSModelDefinitionValidator.Tests
{
    public class ExtractedTableWriterTests
    {
        [Fact]
        public void Writes_original_headers_and_escapes_commas_and_quotes()
        {
            var table = ApiTableConverter.Convert("Wall: Test/Table",
                new[] { "Name", "Note" },
                new Dictionary<string, string> { ["Name"] = "Name", ["Note"] = "Wall Thickness" },
                1, new[] { "W300, A", "say \"hi\"" });

            var csv = ExtractedTableWriter.ToCsv(table);

            Assert.StartsWith("Name,Wall Thickness", csv);
            Assert.Contains("\"W300, A\",\"say \"\"hi\"\"\"", csv);
        }

        [Fact]
        public void WriteAll_creates_one_file_per_table_plus_available_list()
        {
            var folder = Path.Combine(Path.GetTempPath(), "etabs-tables-" + Path.GetRandomFileName());
            try
            {
                var table = ApiTableConverter.Convert("Wall: Test/Table", new[] { "Name" }, null, 1, new[] { "W1" });
                var files = ExtractedTableWriter.WriteAll(folder, new[] { table }, new[] { new EtabsTableInfo { Key = "K", Name = "N" } });

                Assert.Equal(2, files.Count);
                Assert.All(files, f => Assert.True(File.Exists(f)));
                Assert.DoesNotContain(files, f => Path.GetFileName(f).Contains("/") || Path.GetFileName(f).Contains(":"));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
