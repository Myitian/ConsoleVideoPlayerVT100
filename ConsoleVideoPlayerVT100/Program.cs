using System.Diagnostics;

namespace ConsoleVideoPlayerVT100
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.CancelKeyPress += (_, _) =>
            {
                Console.Write("\x1B[0m\nForce exiting...\n");
                Environment.Exit(0);
            };

            string video_file;
            if (args.Length > 0)
            {
                video_file = args[0].Trim();
            }
            else
            {
                Console.WriteLine("Enter video file path:");
                video_file = Console.ReadLine()?.Trim('"').Trim() ?? "video";
            }

            VideoPlayer player = new();

            (int w, int h, string duration, int[]? frame_rate) = player.DetectVideoInfo(video_file);

            double spf;
            int mspf;
            if (frame_rate is null)
            {
                Console.WriteLine("Unknown frame rate.");
                mspf = 40;
                spf = 0.04;
            }
            else
            {
                mspf = 1000 * frame_rate[1] / frame_rate[0];
                spf = (double)frame_rate[1] / frame_rate[0];
            }
            Console.WriteLine($"W: {w}; H: {h}; s/F: {spf}; ms/F: {mspf}");

            (w, h) = CalcOutputSize(w, h);
            Console.WriteLine($"RenderW: {w}; RenderH: {h};");

            long tick_start = DateTime.UtcNow.Ticks;
            double frame_start = 0;
            double avg_tpf = 0;
            long tick_prev = tick_start;
            double tps_d = TimeSpan.TicksPerSecond;
            long tphalfs = TimeSpan.TicksPerSecond / 2;

            Process ffmpeg_dec = player.CreateDecoder(w, h, 0);
            Console.WriteLine(ffmpeg_dec.StartInfo.Arguments);


            string current_time_pos = "N/A";
            Task.Run(() =>
            {
                foreach (string ln in player.DecoderInfoOutputGrabber())
                {
                    int t_start;
                    if (ln.StartsWith("[info]") && (t_start = ln.IndexOf("time=")) >= 0)
                    {
                        t_start += 5;
                        int t_end = ln.IndexOf(' ', t_start);
                        current_time_pos = ln[t_start..t_end].Split('.')[0];
                    }
                }
            });

            IEnumerable<int> frames = player.RenderVideo();
            foreach (int frame in frames)
            {
                long tick_now = DateTime.UtcNow.Ticks;
                long tpf = tick_now - tick_prev;
                long diff_1s = tick_now - tick_start;
                if (diff_1s > tphalfs)
                {
                    avg_tpf = diff_1s / (frame - frame_start);
                    frame_start = frame;
                    tick_start = tick_now;
                }
                Console.Write($"Frame: {frame,7} | FPS: {tps_d / tpf,6:f2} | 0.5s Avg. FPS: {tps_d / avg_tpf,6:f2} | {current_time_pos} / {player.Duration}".PadRight(w));
                tick_prev = tick_now;
            }
            Console.WriteLine("END");
            Thread.Sleep(1000);
            Console.WriteLine("Exiting...");
        }
        static (int, int) CalcOutputSize(int w, int h, double sar = .5)
        {
            int bw = Math.Min(Console.BufferWidth, Console.WindowWidth);
            int bh = Math.Min(Console.BufferHeight, Console.WindowHeight) - 1;

            double rh = h * sar;

            if (w <= bw && rh <= bh)
                return (w, (int)Math.Round(rh));

            double src_wh_ratio = w / rh;
            double dst_wh_ratio = (double)bw / bh;

            if (src_wh_ratio > dst_wh_ratio)
                return (bw, (int)Math.Round(rh * bw / w));
            else
                return ((int)Math.Round(w * bh / rh), bh);

        }
    }
}
