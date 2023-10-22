using System.Diagnostics;
using System.Text;

namespace ConsoleVideoPlayerVT100
{
    public class VideoPlayer
    {
        public ManualResetEventSlim DecoderRunning { get; } = new(false);
        public ManualResetEventSlim RendererIdle { get; } = new(false);
        public Process VideoDecoder { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string Duration { get; private set; }
        public int[]? FrameRate { get; private set; }
        public string VideoFile { get; set; }

        private int renderW, renderH;

        public static void AppendPixel(StringBuilder sb, int r, int g, int b)
        {
            sb.Append("\x1B[48;2;");
            sb.Append(r);
            sb.Append(';');
            sb.Append(g);
            sb.Append(';');
            sb.Append(b);
            sb.Append("m ");
        }
        public (int Witdh, int Height, string Duration, int[]? FrameRate) DetectVideoInfo(string videoFile)
        {
            VideoFile = videoFile;
            Process ffprobe = new()
            {
                StartInfo = new ProcessStartInfo("ffprobe", $"-v warning -select_streams v:0 -sexagesimal -show_entries \"stream=width,height,avg_frame_rate:format=duration\" \"{videoFile}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                }
            };
            ffprobe.Start();
            int width = 0;
            int height = 0;
            string duration = "N/A";
            int[]? frame_rate = null;
            string s;
            string[] sp;
            string? ffp_out;
            while (!string.IsNullOrEmpty(ffp_out = ffprobe.StandardOutput.ReadLine()))
            {
                sp = ffp_out.Split('=');
                s = sp[0];
                switch (s)
                {
                    case "width":
                        _ = int.TryParse(sp[1], out width);
                        break;
                    case "height":
                        _ = int.TryParse(sp[1], out height);
                        break;
                    case "duration":
                        s = sp[1].PadLeft(15, '0');
                        duration = s[..s.IndexOf('.')];
                        break;
                    case "avg_frame_rate":
                        s = sp[1];
                        sp = s.Split('/');
                        if (sp[0] != "0" && sp[1] != "0")
                        {
                            frame_rate = new int[] { int.Parse(sp[0]), int.Parse(sp[1]) };
                        }
                        break;
                }
            }
            ffprobe.Kill();
            return (Width = width, Height = height, Duration = duration, FrameRate = frame_rate);
        }
        public Process CreateDecoder(int w, int h, int skip)
        {
            DecoderRunning.Reset();
            try
            {
                VideoDecoder?.Kill();
            }
            finally
            {
                VideoDecoder?.Close();
            }
            renderW = w;
            renderH = h;
            VideoDecoder = new()
            {
                StartInfo = new ProcessStartInfo("ffmpeg", $"-re -v level+info -an -sn -ss {skip} -i \"{VideoFile}\" -s {w}x{h} -pix_fmt bgra -f rawvideo -")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            VideoDecoder.Start();
            DecoderRunning.Set();
            return VideoDecoder;
        }
        public IEnumerable<string> DecoderInfoOutputGrabber()
        {
            string? ln;
            while ((ln = VideoDecoder.StandardError.ReadLine()) is not null)
            {
                yield return ln;
            }
        }
        public IEnumerable<int> RenderVideo()
        {
            Stream dec_out = VideoDecoder.StandardOutput.BaseStream;
            int read, frame = 0;
            RenderStatus status = RenderStatus.Read_B;
            int x = 0, y = 0, r = 0, g = 0, b = 0;
            StringBuilder sb = new((20 * renderW + 1) * renderH + 300);
            Console.Clear();
            Console.SetCursorPosition(x, y);
            while ((read = dec_out.ReadByte()) >= 0)
            {
                RendererIdle.Reset();
                if (y == renderH)
                {
                    Console.SetCursorPosition(x = 0, y = 0);
                    sb.Clear();
                }
                switch (status)
                {
                    case RenderStatus.Read_B:
                        b = read;
                        status = RenderStatus.Read_G;
                        break;
                    case RenderStatus.Read_G:
                        g = read;
                        status = RenderStatus.Read_R;
                        break;
                    case RenderStatus.Read_R:
                        r = read;
                        status = RenderStatus.Read_A;
                        break;
                    case RenderStatus.Read_A:
                        read = (read << 23) / 255;
                        status = RenderStatus.Read_B;
                        AppendPixel(sb,
                            (r * read) >> 23,
                            (g * read) >> 23,
                            (b * read) >> 23);
                        x++;
                        if (x == renderW)
                        {
                            x = 0;
                            y++;
                            sb.Append("\x1B[0m\n");
                        }
                        if (y == renderH)
                        {
                            Console.Write(sb.ToString());
                            frame++;
                            yield return frame;
                        }
                        DecoderRunning.Wait();
                        break;
                    default:
                        break;
                }
                RendererIdle.Set();
            }
            yield return ++frame;
        }
    }

    public enum RenderStatus : byte
    {
        Read_B,
        Read_G,
        Read_R,
        Read_A,
    }
}
