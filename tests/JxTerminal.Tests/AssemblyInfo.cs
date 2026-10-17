using Microsoft.VisualStudio.TestTools.UnitTesting;

// Avalonia UI dispatcher and static singletons are shared within the process,
// so tests must run sequentially to avoid race conditions on Dispatcher.UIThread.
[assembly: DoNotParallelize]
