using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Internal.Wolkers
{
    /// <summary>
    /// This class implements the IWolker interface — logic for scanning directories.
    /// </summary>
    public class Wolker : IWalker
    {
        /// <summary>
        /// It is the path to dir that we want to scan
        /// </summary>
        public string PathToDir { get; }
        /// <summary>
        /// options for scanning a dir
        /// </summary>
        private EnumerationOptions _options;

        public Wolker(string pathToDir)
        {
            PathToDir = pathToDir ?? throw new ArgumentNullException(nameof(pathToDir));

            // set up otions
            _options = new EnumerationOptions
            {
                RecurseSubdirectories = true, // All Dirs
                IgnoreInaccessible = true, 
                AttributesToSkip = FileAttributes.System, // skip system files
                MatchCasing = MatchCasing.CaseInsensitive
            };
        }
        /// <summary>
        /// This method scans the directory
        /// </summary>
        /// <returns>A collection containing file paths.</returns>
        /// <exception cref="Exception"></exception>
        public IEnumerable<string> Walk()
        {
            throw new NotImplementedException();

        }
    }
}

