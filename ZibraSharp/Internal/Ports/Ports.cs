using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZibraSharp.Internal.Ports
{
    /// <summary>
    /// Walker walks to dir and returns paths to files.
    /// </summary>
    public interface IWalker
    {

        public string PathToDir { get; }
        /// <summary>
        /// Walks to dir and returns paths to files.
        /// </summary>
        /// <param name="dir">Directory to walk.</param>
        /// <returns>Collection of file paths.</returns>
        public IEnumerable<string> Walk();
    }

    /// <summary>
    /// an Interface that provides the logic of an Archiver.
    /// </summary>
    public interface IArchiver
    {
        /// <summary>
        /// it create archive and return a path to that one 
        /// </summary>
        /// <returns>path to the archive</returns>
        public string BeginArchiving();
    }

    /// <summary>
    /// Interface that provides the logging logic for the app
    /// </summary>
    public interface ILogger
    {
        public void Info(string message, Exception e);
        public void Warn(string message, Exception e);
        public void Debug(string message, Exception e);
        public void Error(string message, Exception e);

    }
    /// <summary>
    /// Provides logic for working with the cloud—this could be 
    /// Google Cloud or another cloud platform.
    /// </summary>
    public interface IApiCloud
    {
        /// <summary>
        /// Check if the connection is working
        /// </summary>
        /// <returns>If this method returns true, 
        /// it means the connection is fine.</returns>
        public bool CheckConnection();
        /// <summary>
        /// Upload a file to a cloud
        /// </summary>
        /// <param name="file">this is an archive or a file </param>
        public void UploadFile(Stream file);
    }

    /// <summary>
    /// An interface that provides configuration for this application
    /// </summary>
    public interface IConfig
    {
        /// <summary>
        ///Adding extensions that the application does not scan and does not include in the archive
        /// </summary>
        public void AddExtensions(IEnumerable<string> ext);
        /// <summary>
        /// Remove extensions from the file extensions 
        /// </summary>
        /// <param name="extension"></param>
        public void RemoveExtensions(string extension);
        /// <summary>
        /// A method that loads file extensions into memory
        /// </summary>
        /// <returns>HashSet containing extensions</returns>
        public HashSet<string> GetExtensions();
        /// <summary>
        /// 
        /// </summary>
        public void SetConfig();

    }

    
}
