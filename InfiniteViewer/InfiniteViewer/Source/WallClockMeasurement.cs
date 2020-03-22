using System.Diagnostics;

namespace InfiniteViewer
{
    public class WallClockMeasurement
    {
        public WallClockMeasurement()
        {
            _stopwatch = new Stopwatch();
            _stopwatch.Start();
        }

        public void Report(int threshold_ms, string id)
        {
            _stopwatch.Stop();
            if (_stopwatch.ElapsedMilliseconds >= threshold_ms)
            {
                Debug.WriteLine(id + " took " + _stopwatch.ElapsedMilliseconds + "ms");
            }
            _stopwatch.Restart();
        }

        private Stopwatch _stopwatch;
    }
}