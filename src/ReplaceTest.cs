using System;
using System.IO;

namespace Matterbridge
{
    public class ReplaceTest
    {
        public void ReplaceText(
            String InputFilename,
            String OutputFilename,
            string Version,
            string Description,
            string Author,
            string Name
        )
        {
            Directory.CreateDirectory(System.IO.Directory.GetParent(OutputFilename).FullName);
            string content = File.ReadAllText(InputFilename)
                .Replace("$Version$", Version)
                .Replace("$Description$", Description)
                .Replace("$Name$", Name)
                .Replace("$Modid$", Name)
                .Replace("$Author$", Author);
            File.WriteAllText(
                OutputFilename,
                content
            );
        }
    }
}