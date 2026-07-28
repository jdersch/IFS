/*  
    This file is part of IFS.

    IFS is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    IFS is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with IFS.  If not, see <http://www.gnu.org/licenses/>.
*/

using IFS.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace IFS
{

    public class InvalidConfigurationException : Exception
    {
        public InvalidConfigurationException(string message) : base(message)
        {

        }
    }

    /// <summary>
    /// Configuration information for a virtual server instance
    /// </summary>
    public class ServerConfiguration
    {
        public ServerConfiguration(bool isGatewayServer, HostAddress hostAddress, string ftpRoot, string copyDiskRoot, string bootRoot, string mailRoot) 
        {
            _isGatewayServer = isGatewayServer;
            _hostAddress = hostAddress;
            _ftpRoot = ftpRoot;
            _copyDiskRoot = copyDiskRoot;
            _bootRoot = bootRoot;
            _mailRoot = mailRoot;
        }

        public bool IsGatewayServer => _isGatewayServer;

        public HostAddress HostAddress => _hostAddress;
        public string FTPRoot => _ftpRoot;
        public string CopyDiskRoot => _copyDiskRoot;

        public string BootRoot => _bootRoot;
        public string MailRoot => _mailRoot;

        private bool _isGatewayServer;
        private HostAddress _hostAddress;
        private string _ftpRoot;
        private string _copyDiskRoot;
        private string _bootRoot;
        private string _mailRoot;
    }


    /// <summary>
    /// Encapsulates global server configuration information.
    /// </summary>
    public static class Configuration
    {
        static Configuration()
        {

            MicrocodeBootRequestHack = true;

            ReadConfiguration();

            //
            // Ensure that required values were read from the config file.  If not,
            // throw so that startup is aborted.
            //
            foreach (string root in FTPRoots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    throw new InvalidConfigurationException($"FTP root path '{root}' is invalid.");
                }
            }

            if (string.IsNullOrWhiteSpace(CopyDiskRoot) || !Directory.Exists(CopyDiskRoot))
            {
                throw new InvalidConfigurationException($"CopyDisk root path '{CopyDiskRoot}' is invalid.");
            }

            if (string.IsNullOrWhiteSpace(BootRoot) || !Directory.Exists(BootRoot))
            {
                throw new InvalidConfigurationException($"Boot root path '{BootRoot}' is invalid.");
            }

            if (string.IsNullOrWhiteSpace(MailRoot) || !Directory.Exists(MailRoot))
            {
                throw new InvalidConfigurationException($"Mail root path '{MailRoot}' is invalid.");
            }

            // Ensure sane values for host numbers:
            // Check for duplicates:
            if (ServerHosts.Length != ServerHosts.Distinct().Count())
            {
                throw new InvalidConfigurationException("Duplicated entry in ServerHosts");
            }

            _serverConfigurations = new List<ServerConfiguration>();
            foreach (byte host in ServerHosts)
            {
                // Disallow host 0 and 377 (broadcast, BOL)
                if (host == 0 || host == 255)
                {
                    throw new InvalidConfigurationException($"Invalid reserved host address {host} specified in ServerHosts");
                }

                _serverConfigurations.Add(GetServerConfiguration(_serverConfigurations.Count()));
            }

            if (MaxWorkers < 1)
            {
                throw new InvalidConfigurationException("MaxWorkers must be >= 1.");
            }

            if (UDPPort == 0)
            {
                // Set to default.
                UDPPort = 42424;
            }
        }

        /// <summary>
        /// The type of interface(s) (UDP, RAW, or 3mbit) to communicate over
        /// </summary>
        public static readonly string InterfaceTypes;

        /// <summary>
        /// The name of the network interface to use
        /// </summary>
        public static readonly string InterfaceName;

        /// <summary>
        /// The UDP port to use when using a UDP interface.
        /// </summary>
        public static readonly int UDPPort;

        /// <summary>
        /// Whether to run IFS Services or just bridge interfaces.
        /// </summary>
        public static readonly bool RunIFSServices;

        /// <summary>
        /// The network that this server lives on
        /// </summary>
        private static readonly byte ServerNetwork;

        /// <summary>
        /// The host numbers for the servers.
        /// </summary>
        private static readonly byte[] ServerHosts;

        /// <summary>
        /// The root directories for the FTP file stores for each server.
        /// </summary>
        private static readonly string[] FTPRoots;

        /// <summary>
        /// The root directory for the CopyDisk file store (only one per IFS instance)
        /// </summary>
        private static readonly string CopyDiskRoot;

        /// <summary>
        /// The root directory for the Boot file store (only one per IFS instance)
        /// </summary>
        private static readonly string BootRoot;

        /// <summary>
        /// The root directory for the Mail file store (only one per IFS instance)
        /// </summary>
        private static readonly string MailRoot;

        /// <summary>
        /// The maximum number of worker threads for protocol handling.
        /// </summary>
        public static readonly int MaxWorkers = 256;

        /// <summary>
        /// The components to display logging messages for.
        /// </summary>
        public static readonly LogComponent LogComponents;

        /// <summary>
        /// The type (Verbosity) of messages to log.
        /// </summary>
        public static readonly LogType LogTypes;

        /// <summary>
        /// The delay (in msec) between Breath Of Life packets
        /// </summary>
        public static readonly int BOLDelay;

        /// <summary>
        /// Temporary:
        /// Whether to send an extra (bogus) packet for Initial.eb or not
        /// </summary>
        public static readonly bool MicrocodeBootRequestHack;

        /// <summary>
        /// Individual "virtual" server configurations:
        /// </summary>
        public static IReadOnlyList<ServerConfiguration> ServerConfigurations => _serverConfigurations;


        /// <summary>
        /// Returns an oh-so convenient configuration object for the server at the specified index.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static ServerConfiguration GetServerConfiguration(int index)
        {
            if (index < 0 || index > ServerHosts.Length -1)
            {
                throw new ArgumentOutOfRangeException("index");
            }

            return new ServerConfiguration(
                index == 0,     // Only the first server specified provides gateway services.
                new HostAddress(ServerNetwork, ServerHosts[index]),
                FTPRoots[index],
                CopyDiskRoot,
                BootRoot,
                MailRoot);
        }

        private static void ReadConfiguration()
        {
            using (StreamReader configStream = new StreamReader(Path.Combine("Conf", "ifs.cfg")))
            {
                //
                // Config file consists of text lines containing name / value pairs:
                //      <Name>=<Value>
                // Whitespace is ignored.
                //
                int lineNumber = 0;
                while (!configStream.EndOfStream)
                {
                    lineNumber++;
                    string line = configStream.ReadLine().Trim();

                    if (string.IsNullOrEmpty(line))
                    {
                        // Empty line, ignore.
                        continue;
                    }

                    if (line.StartsWith("#"))
                    {
                        // Comment to EOL, ignore.
                        continue;
                    }

                    // Find the '=' separating tokens and ensure there are just two.
                    string[] tokens = line.Split(new char[] { '=' }, StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length < 2)
                    {
                        Log.Write(LogType.Warning, LogComponent.Configuration,
                            "ifs.cfg line {0}: Invalid syntax.", lineNumber);
                        continue;
                    }

                    string parameter = tokens[0].Trim();
                    string value = tokens[1].Trim();

                    // Reflect over the public, static properties in this class and see if the parameter matches one of them
                    // If not, it's an error, if it is then we attempt to coerce the value to the correct type.
                    // TODO: do this by attribute
                    System.Reflection.FieldInfo[] info = typeof(Configuration).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    bool bMatch = false;
                    foreach (FieldInfo field in info)
                    {
                        // Case-insensitive compare.
                        if (field.Name.ToLowerInvariant() == parameter.ToLowerInvariant())
                        {
                            bMatch = true;

                            //
                            // Switch on the type of the field and attempt to convert the value to the appropriate type.
                            // At this time we support only strings and integers.
                            //
                            try
                            {
                                switch (field.FieldType.Name)
                                {
                                    case "Boolean":
                                        {
                                            bool b = bool.Parse(value);
                                            field.SetValue(null, b);
                                        }
                                        break;

                                    case "Byte":
                                        {
                                            byte v = Convert.ToByte(value, 8);
                                            field.SetValue(null, v);
                                        }
                                        break;

                                    case "Int32":
                                        {
                                            int v = int.Parse(value);
                                            field.SetValue(null, v);
                                        }
                                        break;

                                    case "String":
                                        {
                                            field.SetValue(null, value);
                                        }
                                        break;

                                    case "LogType":
                                        {
                                            field.SetValue(null, Enum.Parse(typeof(LogType), value, true));
                                        }
                                        break;

                                    case "LogComponent":
                                        {
                                            field.SetValue(null, Enum.Parse(typeof(LogComponent), value, true));
                                        }
                                        break;

                                    case "Byte[]":
                                        {
                                            string[] values = value.Split(',');
                                            List<byte> ints = new List<byte>(values.Length);

                                            foreach (string v in values)
                                            {
                                                ints.Add(Convert.ToByte(v, 8));
                                            }

                                            field.SetValue(null, ints.ToArray());
                                        }
                                        break;

                                    case "String[]":
                                        {
                                            string[] values = value.Split(',');
                                            field.SetValue(null, values.ToArray());
                                        }
                                        break;

                                }
                            }
                            catch
                            {
                                Log.Write(LogType.Warning, LogComponent.Configuration,
                                    "ifs.cfg line {0}: Value '{1}' is invalid for parameter '{2}'.", lineNumber, value, parameter);
                            }
                        }
                    }

                    if (!bMatch)
                    {
                        Log.Write(LogType.Warning, LogComponent.Configuration,
                            "ifs.cfg line {0}: Unknown configuration parameter '{1}'.", lineNumber, parameter);
                    }
                }
            }
        }

        private static List<ServerConfiguration> _serverConfigurations;
    }
}
