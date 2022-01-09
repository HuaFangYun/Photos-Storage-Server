using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Npgsql;
using Microsoft.VisualBasic.FileIO;
using SearchOption = System.IO.SearchOption;

namespace PSS.Backend
{
    /// <summary>
    /// Functions for server maintenance.
    /// </summary>
    public static class Maintenance
    {
        /// <summary>
        /// Backup library and database on server.
        /// </summary>
        public static void BackupServer()
        {
            //Clear backup folder to remove old backup: https://stackoverflow.com/a/12297082
            DirectoryInfo di = new(Settings.backupFolderPath);
            di.Delete(true);
            Directory.CreateDirectory(Settings.backupFolderPath); 
            
            FileSystem.CopyDirectory(Settings.libFolderFullPath, Path.Combine(Settings.backupFolderPath, "PSS Media Backup"));
            File.WriteAllText(Path.Combine(Settings.backupFolderPath, "Backed up on.txt"), DateTime.Now.ToString("M-d-yyyy h:mm:ss tt"));
            
            Process process = new(); //Backup entire database to a file
            ProcessStartInfo startInfo = new()
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = "C:/Program Files/PostgreSQL/14/bin/",
                FileName = "cmd.exe",
                Arguments = $"/C {Settings.databaseBackupCommand}"
            };
            process.StartInfo = startInfo;
            process.Start(); //How to run this cmd without a password prompt: https://stackoverflow.com/a/62417775
            process.WaitForExit();
        }
        
        /// <summary>
        /// Return if a folder is empty (has 0 files in folder or subfolders). Subfolders inside path aren't counted unless they have stuff inside.
        /// If root/ has 2 subfolders and 3 files it would only find 3 files and thus it wouldn't be counted as empty.
        /// If root/ has 2 subfolders and 0 files, it would be considered empty.
        /// </summary>
        /// <returns>True if empty.</returns>
        public static bool IsFolderEmpty(string path) => Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length == 0;

        /// <summary>
        /// Search library folder and if an item is not in the media or media_trash tables, add it to the List of full paths that is returned. 
        /// </summary>
        public static List<string> GetUntrackedLibFiles()
        {
            List<string> untrackedPaths = new(); //Items in lib folder but not in database
            string[] paths = Directory.GetFiles(Settings.libFolderFullPath, "*", SearchOption.AllDirectories);
            List<string> mediaPaths = Connection.LoadMediaTable().Select(media => media.path).ToList(); //Get just paths
            List<string> mediaTrashPaths = Connection.LoadMediaTrashTable().Select(media => media.path).ToList();

            foreach (string fullPath in paths)
            {
                string shortPath = fullPath.Replace(Settings.libFolderFullPath, "");
                if (shortPath.StartsWith('\\') || shortPath.StartsWith('/')) shortPath = shortPath[1..];
                
                if (!mediaPaths.Contains(shortPath) && !mediaTrashPaths.Contains(shortPath)) //If find an item not in library add to list
                    untrackedPaths.Add(fullPath);
            }
            return untrackedPaths;
        }

        /// The table to search for missing files.        
        public enum MissingFilesTable
        {
            media,
            media_trash,
            album_entries,
            album_entries_trash
        }

        /// <summary>
        /// Return string List of all shortPaths from the table specified that don't have existing files in the photo library.
        /// </summary>
        public static List<string> GetMissingFiles(MissingFilesTable table)
        {
            string tableStr = table.ToString();
            List<string> missingFiles = new();

            try
            {
                Connection.Open();
                NpgsqlCommand cmd = new("SELECT path FROM " + tableStr, Connection.connection);
                cmd.ExecuteNonQuery();
                NpgsqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    string shortPath = reader.GetString(0);
                    string fullPath = Path.Combine(Settings.libFolderFullPath, shortPath);
                    if (!File.Exists(fullPath))
                        missingFiles.Add(shortPath);
                }
                reader.Close();
            }
            catch (NpgsqlException e)
            {
                Console.WriteLine("An unknown error occurred. Error code: " + e.ErrorCode + " Message: " + e.Message);
            }
            finally
            {
                Connection.Close();
            }

            return missingFiles;
        }

        /// <summary>
        /// Loop through List and delete these paths from table that are in the DB but don't exist as files.
        /// </summary>
        /// <param name="paths">List of shortPaths retrieved with GetMissingFiles()</param>
        /// <param name="table">The table to delete from</param>
        public static void RemoveMissingFiles(List<string> paths, MissingFilesTable table)
        {
            try
            {
                Connection.Open();
                foreach (string path in paths)
                {
                    NpgsqlCommand cmd = new("DELETE FROM " + table + " WHERE path=@path", Connection.connection);
                    cmd.Parameters.AddWithValue("@path", path);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (NpgsqlException e)
            {
                Console.WriteLine("An unknown error occurred. Error code: " + e.ErrorCode + " Message: " + e.Message);
            }
            finally
            {
                Connection.Close();
            }
        }
    }
}