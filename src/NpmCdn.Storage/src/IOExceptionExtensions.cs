namespace NpmCdn.Storage;

public static class IOExceptionExtensions
{
    extension(IOException ex)
    {
        /// <summary>
        /// Determines if an IOException is the result of a file lock or sharing violation.
        /// </summary>
        /// <returns>True if the exception is due to a file lock; otherwise, false.</returns>
        public bool IsFileLockException()
        {
            // Extract the Win32 error code from the HResult.
            // 32 = ERROR_SHARING_VIOLATION: The process cannot access the file because it is being used by another process.
            // 33 = ERROR_LOCK_VIOLATION: The process cannot access the file because another process has locked a portion of the file.
            int errorCode = ex.HResult & 0xFFFF;
            return errorCode is 32 or 33;
        }
    }
}