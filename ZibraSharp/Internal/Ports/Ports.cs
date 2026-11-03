using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZibraSharp.Internal.Ports
{
    // Walker walk to dir and returns pathes to files
    public interface IWalker
    {
        // This dir we will scan and send to IArchiver
        public string PathToDir { get; }
        public string Walk();
    }

    // an Interface that provides the logic of an Archiver.
    public interface IArchiver
    {
        //it create archive and return a path to that one 
        public string BeginArchiving();
    }

    // Interface that provides the logging logic for the app
    public interface ILogger
    {
        public void Info(string message, Exception e);
        public void Warn(string message, Exception e);
        public void Debug(string message, Exception e);
        public void Error(string message, Exception e);

    }

    
}
