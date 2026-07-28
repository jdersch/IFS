using Microsoft.Extensions.FileSystemGlobbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFS.FTP
{
    public struct FileInfo :  IComparable
    {
        public string FullPath;
        public string FileName;
        public int Revision;

        public static bool operator <(FileInfo a, FileInfo b)
        {
            return a.Revision < b.Revision;
        }
        public static bool operator >(FileInfo a, FileInfo b)
        {
            return a.Revision > b.Revision;
        }

        public static bool operator ==(FileInfo a, FileInfo b)
        {
            return a.Revision == b.Revision;
        }

        public static bool operator !=(FileInfo a, FileInfo b)
        {
            return a.Revision != b.Revision;
        }

        public int CompareTo(object obj)
        {
            FileInfo other = (FileInfo)obj;
            if (other == this)
            {
                return 0;
            }
            else if (other > this)
            {
                return -1;
            }
            
            return 1;
        }
    }

    public class VersionedDirectory
    {
        public static string GetPathWithoutRevision(string fileName)
        {
            int revisionSpecifierIndex = GetRevisionCharacterIndex(fileName);

            if (revisionSpecifierIndex != -1)
            {
                return fileName.Substring(0, revisionSpecifierIndex);
            }

            return fileName;
        }

        public static int GetPathRevision(string fileName)
        {
            RevisionTagFormat tagFormat = GetRevisionTagFormat(fileName);
            int revisionSpecifierIndex = GetRevisionCharacterIndex(fileName);

            if (revisionSpecifierIndex == -1)
            {
                // No revision specified in name.  Return -1 to explicitly denote that the file has no revision tag.
                return -1;
            }

            // Increment the index for tilde-format tags (number starts after the ~, index is currently at .
            // this is the worst
            if (tagFormat == RevisionTagFormat.Tilde)
            {
                revisionSpecifierIndex++;
            }

            int revision = -1;
            if (!int.TryParse(fileName.Substring(revisionSpecifierIndex + 1), out revision))
            {
                // Revision specifier is bogus...
                return -1;
            }

            return revision;
        }

        public VersionedDirectory(string path)
        {
            m_path = path;
            m_completeFileList = GetFiles(path);
        }

        public List<FileInfo> GetAllFiles()
        {
            return m_completeFileList;
        }

        public List<FileInfo> GetLatestFiles()
        {
            return m_completeFileList.Select(info => GetLatestRevisionOfFile(m_completeFileList, info)).Distinct().ToList();
        }

        public List<FileInfo> GetFilesAtSpecifiedRevision(int revision)
        {
            return m_completeFileList.Where(info => info.Revision == revision).ToList();
        }

        public FileInfo CreateNewRevisionForFile(FileInfo existingFile)
        {
            var newRevision = GetLatestRevisionOfFile(m_completeFileList, existingFile).Revision + 1;
            var newFile = new FileInfo()
            {
                FullPath = GetPathWithoutRevision(existingFile.FullPath) + $"!{newRevision}",
                FileName = existingFile.FileName,
                Revision = newRevision
            };

            File.Create(newFile.FullPath);

            m_completeFileList.Add(newFile);

            return newFile;
        }

        public void DeleteFile(FileInfo fileToDelete)
        {
            File.Delete(fileToDelete.FullPath);
            m_completeFileList.Remove(fileToDelete);
        }

        public void ExpungeFile(FileInfo fileToDelete)
        {

        }

        /// <summary>
        /// Returns a list of FileInfos describing files that match the given pattern, either at the specified revision
        /// or the latest revision, if unspecified.
        /// </summary>
        /// <param name="pattern"></param>
        /// <param name="revision"></param>
        /// <returns></returns>
        public List<FileInfo> GetMatchingFiles(string pattern, int revision = -1)
        {
            Matcher fileMatcher = new Matcher();
            fileMatcher.AddInclude(pattern);

            var revisionFiles = revision != -1 ? GetFilesAtSpecifiedRevision(revision) : GetLatestFiles();
            var revisionFileNames = revisionFiles.Select(n => n.FileName);

            var matchingFiles = new List<FileInfo>();
            foreach (var file in fileMatcher.Match(revisionFileNames).Files)
            {
                // We have to go back through and find the matched filename in the list of files in the directory so we can return them.
                // This is less than optimal.  Possibly this could be avoided by having VersionedDirectory subclass DirectoryInfoBase
                // and doing this a somewhat different way; as it is this is N^2 which given the number of files involved, meh.
                matchingFiles.Add(revisionFiles.Where(a => a.FileName == file.Path).First());
            }

            return matchingFiles;
        }

        private enum RevisionTagFormat
        {
            None,
            Exclamation,
            Tilde
        }

        private static RevisionTagFormat GetRevisionTagFormat(string fileName)
        {
            // Try the format "filename.ext!version":
            int periodIndex = fileName.LastIndexOf('.');
            int revisionSpecifierIndex = fileName.LastIndexOf('!');

            if (revisionSpecifierIndex != -1 && revisionSpecifierIndex > periodIndex)
            {
                return RevisionTagFormat.Exclamation;
            }

            // "filename.ext.~version~" is sometimes used...
            revisionSpecifierIndex = fileName.LastIndexOf(".~");

            if (revisionSpecifierIndex != -1)
            {
                return RevisionTagFormat.Tilde;
            }

            return RevisionTagFormat.None;
        }

        private static int GetRevisionCharacterIndex(string fileName)
        {
            RevisionTagFormat tagFormat = GetRevisionTagFormat(fileName);

            switch (tagFormat)
            {
                case RevisionTagFormat.Exclamation:
                    return fileName.LastIndexOf('!');

                case RevisionTagFormat.Tilde:
                    return fileName.LastIndexOf(".~");

                default:
                    return -1;
            }
        }

        private List<FileInfo> GetFiles(string path)
        {
            string[] filePaths = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
            List<FileInfo> files = new List<FileInfo>(filePaths.Length);

            foreach (string f in filePaths)
            {
                FileInfo info = new FileInfo()
                {
                    FullPath = f,
                    FileName = GetPathWithoutRevision(Path.GetFileName(f)),
                    Revision = GetPathRevision(Path.GetFileName(f)),
                };

                files.Add(info);
            }

            return files;
        }

        private FileInfo GetLatestRevisionOfFile(List<FileInfo> fromFiles, FileInfo file)
        {
            var matchingFiles = fromFiles.Where(f => string.Equals(f.FileName, file.FileName, StringComparison.InvariantCultureIgnoreCase)).ToList();
            return matchingFiles.Max();
        }

        private string m_path;
        private List<FileInfo> m_completeFileList;

    }


}
