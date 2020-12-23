using LeiKaiFeng.Http;
using SQLite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace Pixiv
{


    static class CreatePixivData
    {
        const string ORIGINAL_BASE_PATH = "https://i.pximg.net/img-original/img/";

        static readonly Regex s_re_original = new Regex(@"""original"":""([^""]+)""");

        //Regex m_re_small = new Regex(@"""small"":""([^""]+)""");

        static readonly Regex s_re_mark = new Regex(@"""bookmarkCount"":(\d+),");

        static readonly Regex s_re_tags = new Regex(@"""tags"":\[([^\]]+)\],""userId""");



        static string F(Regex re, string html)
        {
            Match match = re.Match(html);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            else
            {
                throw new FormatException();
            }
        }

        static string GetPath(string s)
        {

            return s.Substring(ORIGINAL_BASE_PATH.Length);
        }

        public static PixivData Create(int itemId, string html)
        {

            try
            {

                int mark = int.Parse(F(s_re_mark, html));

                string path = GetPath(F(s_re_original, html));

                string tags = F(s_re_tags, html);

                return new PixivData(itemId, mark, path, tags);
            }
            catch(FormatException)
            {
                throw;
            }
            catch(Exception e)
            {
                throw new FormatException("", e);
            }

        }

        public static Uri GetOriginalUri(string path)
        {
            return new Uri(ORIGINAL_BASE_PATH + path);
        }

        static string WithOut(string s)
        {
            int index = s.IndexOf(".", StringComparison.OrdinalIgnoreCase);

            if (index == -1)
            {
                return s;
            }
            else
            {

                return s.Substring(0, index);
            }

        }

        public static Uri GetSmallUri(PixivData data)
        {
            return new Uri("https://i.pximg.net/c/540x540_70/img-master/img/" + WithOut(data.Path) + "_master1200.jpg");
        }


        public static Uri GetNextUri(int value)
        {
            return new Uri("https://www.pixiv.net/artworks/" + value);
        }

    }

    static class CreatePixivMHttpClient
    {
        const string HOST = "www.pixivision.net";

        static Task CreateConnectAsync(Socket socket, Uri uri)
        {
            return socket.ConnectAsync(HOST, 443);
        }

        static async Task<Stream> CreateAuthenticateAsync(Stream stream, Uri uri)
        {

            SslStream sslStream = new SslStream(stream, false);

            await sslStream.AuthenticateAsClientAsync(HOST).ConfigureAwait(false);

            return sslStream;
        }


        public static MHttpClient Create(int maxStreamPoolCount)
        {
            return new MHttpClient(new MHttpClientHandler
            {
                ConnectCallback = CreateConnectAsync,

                AuthenticateCallback = CreateAuthenticateAsync,

                MaxStreamPoolCount = maxStreamPoolCount
            });
        }
    }


    public sealed class PixivData
    {
        public PixivData()
        {
        }

        public PixivData(int itemId, int mark, string path, string tags)
        {
            ItemId = itemId;

            Mark = mark;

            Path = path;

            Tags = tags;
        }

        [PrimaryKey]
        public int ItemId { get; set; }


        public int Mark { get; set; }

        [MaxLength(40)]
        public string Path { get; set; }

        [MaxLength(200)]
        public string Tags { get; set; }
    }

    static class DataBase
    {
        const int START_VALUE = 66000201;

        const string DatabaseFilename = "PixivBaseData.db3";

        const SQLite.SQLiteOpenFlags Flags =

            SQLite.SQLiteOpenFlags.ReadWrite |

            // create the database if it doesn't exist
            SQLite.SQLiteOpenFlags.Create |
            // enable multi-threaded database access
            SQLite.SQLiteOpenFlags.SharedCache |

            SQLite.SQLiteOpenFlags.FullMutex;

        static SQLiteConnection s_connection;

        static SemaphoreSlim s_slim;

        static string s_basePath;

        static string DatabasePath()
        {
            
            return Path.Combine(s_basePath, DatabaseFilename);

        }


        public static void Init(string basePath)
        {
            s_basePath = basePath;

            Directory.CreateDirectory(s_basePath);

            s_slim = new SemaphoreSlim(1, 1);

            s_connection = new SQLiteConnection(DatabasePath(), Flags);

            s_connection.CreateTable<PixivData>();
        }

        static int GetMaxItemId_()
        {
            var datas = s_connection.DeferredQuery<PixivData>($"SELECT {nameof(PixivData.ItemId)} FROM {nameof(PixivData)} ORDER BY {nameof(PixivData.ItemId)} DESC");

            int n = datas.Select((d) => d.ItemId).FirstOrDefault();

            return n == 0 ? START_VALUE : n;
        }

        static object Add_(PixivData data)
        {
            s_connection.InsertOrReplace(data);

            return null;
        }

        static object Add_(IEnumerable<PixivData> datas)
        {
            foreach (var item in datas)
            {
                s_connection.InsertOrReplace(item);
            }

            return null;
        }


        static List<PixivData> Select_(int minMark, int maxMark, int offset, int count)
        {
            return s_connection.Query<PixivData>($"SELECT * FROM {nameof(PixivData)} WHERE {nameof(PixivData.Mark)} >= {minMark} AND {nameof(PixivData.Mark)} <= {maxMark} ORDER BY {nameof(PixivData.Mark)} DESC LIMIT {count} OFFSET {offset}");
        }

        static List<PixivData> Select_(int minMark, int maxMark, string tag, int offset, int count)
        {
            return s_connection.Query<PixivData>($"SELECT * FROM {nameof(PixivData)} WHERE {nameof(PixivData.Mark)} >= {minMark} AND {nameof(PixivData.Mark)} <= {maxMark} AND {nameof(PixivData.Tags)} LIKE '%{tag}%' ORDER BY {nameof(PixivData.Mark)} DESC LIMIT {count} OFFSET {offset}");
        }

        static Task<T> F<T>(Func<T> func)
        {
            return Task.Run(() => FF(func));
        }

        static async Task<T> FF<T>(Func<T> func)
        {

            try
            {
                await s_slim.WaitAsync().ConfigureAwait(false);


                return func();
            }
            finally
            {
                s_slim.Release();
            }

        }


        public static Task<List<PixivData>> Select(int minMark, int maxMark, int offset, int count)
        {
            return F(() => Select_(minMark, maxMark, offset, count));
        }

        public static Task<List<PixivData>> Select(int minMark, int maxMark, string tag, int offset, int count)
        {
            return F(() => Select_(minMark, maxMark, tag, offset, count));
        }


        public static Task<int> GetMaxItemId()
        {
            return F(GetMaxItemId_);
        }


        public static Task Add(IEnumerable<PixivData> datas)
        {
            return F(() => Add_(datas));
        }

        public static Task Add(PixivData data)
        {
            return F(() => Add_(data));
        }
    }


    static class Crawling
    {
        static async void Start(Func<Task<PixivData>> func)
        {
            
            while (true)
            {
               
                try
                {


                    PixivData data = await func().ConfigureAwait(false);
                   
                    await DataBase.Add(data).ConfigureAwait(false);
                }
                catch (MHttpClientException)
                {

                }
                catch (FormatException)
                {

                }
                
            }
        }


        static void Start(int id, int count)
        {
            MHttpClient httpClient = CreatePixivMHttpClient.Create(count);


            Func<Task<PixivData>> func = async () =>
            {
                int n = Interlocked.Increment(ref id);

                Uri uri = CreatePixivData.GetNextUri(n);

                string html = await httpClient.GetStringAsync(uri).ConfigureAwait(false);

                return CreatePixivData.Create(n, html);
            };


            foreach (var item in Enumerable.Range(0, count))
            {
                Start(func);
            }
        }

        public static void Start(int count)
        {

            Task.Run(() =>
            {
                DataBase.GetMaxItemId().ContinueWith((t) => Start(t.Result, count));
            });

        }
    }

    sealed class LoadBigImg
    {
        readonly MHttpClient m_client;

        readonly string m_basePath;
        public LoadBigImg(string basePath)
        {
            m_client = new MHttpClient(new MHttpClientHandler
            {
                MaxResponseSize = 1024 * 1024 * 50
            });

            Directory.CreateDirectory(basePath);

            m_basePath = basePath;
        }

        async Task SaveImage(byte[] buffer)
        {
            string name = Path.Combine(m_basePath, Path.GetRandomFileName() + ".png");

            using (var file = new FileStream(name, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {

                await file.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            }
        }

        async void Load(string path)
        {
            try
            {

                Uri uri = CreatePixivData.GetOriginalUri(path);


                Uri referer = new Uri("https://www.pixiv.net/");


                byte[] buffer = await m_client.GetByteArrayAsync(uri, referer).ConfigureAwait(false);

                await SaveImage(buffer).ConfigureAwait(false);
            }
            catch
            {

            }

        }

        public void Add(string path)
        {
            Task.Run(() => Load(path));
        }
    }

    static class EnumImage
    {
        


        static Task<byte[]> GetImageFromWebAsync(MHttpClient client, PixivData data)
        {
            Uri uri = CreatePixivData.GetSmallUri(data);


            Uri referer = new Uri("https://www.pixiv.net/");


            return client.GetByteArrayAsync(uri, referer);

        }

        static async void CreateLoadImg(MHttpClient client, MyChannels<Task<PixivData>> pixivDatas, MyChannels<Task<KeyValuePair<byte[], string>>> imgs)
        {
            while (true)
            {
                try
                {

                    var data = await (await pixivDatas.ReadAsync().ConfigureAwait(false)).ConfigureAwait(false);

                    byte[] buffer = await GetImageFromWebAsync(client, data).ConfigureAwait(false);


                    await imgs.WriteAsync(Task.FromResult(new KeyValuePair<byte[], string>(buffer, data.Path))).ConfigureAwait(false);

                }
                catch (Exception e)
                {

                    await imgs.WriteAsync(Task.FromException<KeyValuePair<byte[], string>>(e)).ConfigureAwait(false);
                }

            }
        }

        static async void CreateLoadData(MyChannels<Task<PixivData>> coll, Func<Task<List<PixivData>>> func)
        {
            try
            {

                while (true)
                {

                    var list = await func().ConfigureAwait(false);

                    if (list.Count == 0)
                    {
                        return;
                    }

                    foreach (var item in list)
                    {
                        await coll.WriteAsync(Task.FromResult(item)).ConfigureAwait(false);
                    }

                }
            }
            catch(Exception e)
            {
                await coll.WriteAsync(Task.FromException<PixivData>(e)).ConfigureAwait(false);
            }

        }


        public static MyChannels<Task<KeyValuePair<byte[], string>>> Create(Func<Task<List<PixivData>>> func, int dataLoadCount, int imgLoadCount)
        {

            var datas = new MyChannels<Task<PixivData>>(dataLoadCount);

            var imgs = new MyChannels<Task<KeyValuePair<byte[], string>>>(imgLoadCount);

            var mhttpclient = new MHttpClient(new MHttpClientHandler
            {
                MaxStreamPoolCount = imgLoadCount * 2
            });


            Task.Run(() => CreateLoadData(datas, func));

            foreach (var item in Enumerable.Range(0, imgLoadCount)) 
            {
                Task.Run(() => CreateLoadImg(mhttpclient, datas, imgs));
            }

            return imgs;
        }
    }


    sealed class Data
    {
        public Data(byte[] buffer, string path)
        {
            Path = path;

            Buffer = buffer;

            ImageSource = ImageSource.FromStream(() => new MemoryStream(Buffer));
        }

        public string Path { get; }

        public ImageSource ImageSource { get; }

        public byte[] Buffer { get; }


    }

    static class InputData
    {
        public static int Min
        {
            get => Preferences.Get(nameof(Min), 0);

            set=> Preferences.Set(nameof(Min), value);
        }

        public static int Max
        {
            get => Preferences.Get(nameof(Max), 10000);

            set => Preferences.Set(nameof(Max), value);
        }

        public static string Tag
        {
            get => Preferences.Get(nameof(Tag), "");

            set => Preferences.Set(nameof(Tag), value);
        }

        public static int Offset
        {
            get => Preferences.Get(nameof(Offset), 0);

            set => Preferences.Set(nameof(Offset), value);
        }

        public static int Count
        {
            get => Preferences.Get(nameof(Count), 0);

            set => Preferences.Set(nameof(Count), value);
        }

        static int F(string s)
        {
            if (int.TryParse(s, out int n) && n >= 0)
            {
                return n;
            }
            else
            {
                throw new FormatException();
            }

        }

        public static bool Create(string min, string max, string tag, string offset, string count)
        {
            try
            {
                Min = F(min);

                Max = F(max);

                Offset = F(offset);

                Count = F(count);


                Tag = tag ?? "";

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static Func<Task<List<PixivData>>> CreateSelectFunc()
        {
            string tag = Tag;

            int min = Min;

            int max = Max;

            int offset = Offset;

            int count = Count;



            return () =>
            {
                int n = offset;

                Task<List<PixivData>> task;
                if (string.IsNullOrWhiteSpace(tag))
                {
                    task = DataBase.Select(min, max, offset, count);

                }
                else
                {
                    task = DataBase.Select(min, max, tag, offset, count);

                }

                offset += count;

                Offset = n;

                return task;

            };
        }
    }

    public partial class MainPage : ContentPage
    {
        const int COLLVIEW_COUNT = 32;

        const int SELCT_COUNT = 200;

        const int CRAWLING_COUNT = 16;

        const string ROOT_PATH = "/storage/emulated/0/pixiv/";

        const string BASE_PATH = ROOT_PATH + "database/";
      
        const string IMG_PATH = ROOT_PATH + "img/";


        readonly ObservableCollection<Data> m_imageSources = new ObservableCollection<Data>();

        readonly LoadBigImg m_download = new LoadBigImg(IMG_PATH);

        public MainPage()
        {
            InitializeComponent();

            DeviceDisplay.KeepScreenOn = true;

            Init();
        }

        async void Init()
        {
            try
            {
                var p = await Permissions.RequestAsync<Permissions.StorageWrite>();

                if (p == PermissionStatus.Granted)
                {

                    Directory.CreateDirectory(ROOT_PATH);

                    Directory.CreateDirectory(IMG_PATH);
                }

            }
            catch
            {
                Task t = DisplayAlert("错误", "需要存储权限", "确定");

                return;
            }




            DataBase.Init(BASE_PATH);

            Crawling.Start(CRAWLING_COUNT);

            InitInputView();

            InitViewText();

            InitCollView();
        } 

        void InitInputView()
        {
            m_tag_value.Text = InputData.Tag;

            m_min_value.Text = InputData.Min.ToString();
           
            m_max_value.Text = InputData.Max.ToString();
            
            m_offset_value.Text = InputData.Offset.ToString();
           
        }

        bool CreateInput()
        {
            return InputData.Create(m_min_value.Text, m_max_value.Text, m_tag_value.Text, m_offset_value.Text, SELCT_COUNT.ToString());
        }


        async void InitViewText()
        {
            while (true)
            {
                m_viewText.Text = (await DataBase.GetMaxItemId()).ToString();

                await Task.Delay(new TimeSpan(0, 0, 5));
            }
        } 

        void InitCollView()
        {
            m_collView.ItemsSource = m_imageSources;
        }

        void OnStart(object sender, EventArgs e)
        {
            
            if (CreateInput() == false)
            {
                Task t = DisplayAlert("错误", "必须输入参数", "确定");
            }
            else
            {
                m_cons.IsVisible = false;

                Start();
            }
        }


        async Task FlushView()
        {
            await Task.Yield();
        }

        Task SetImage(Data date)
        {

            if (m_imageSources.Count >= COLLVIEW_COUNT)
            {
                m_imageSources.Clear();
            }

            m_imageSources.Add(date);


            m_collView.ScrollTo(m_imageSources.Count - 1, position: ScrollToPosition.End, animate: false);

            return FlushView();
        }

        static void Log(string name, object e)
        {
            string s = System.Environment.NewLine;

            File.AppendAllText($"/storage/emulated/0/pixiv.{name}.txt", $"{s}{s}{s}{s}{DateTime.Now}{s}{e}", System.Text.Encoding.UTF8);
        }

        async void Start()
        {

            var imgs = EnumImage.Create(InputData.CreateSelectFunc(), 64, COLLVIEW_COUNT);

            while (true)
            {

                try
                {

                    var item = await (await imgs.ReadAsync());

                    await SetImage(new Data(item.Key, item.Value));

                    await Task.Delay(new TimeSpan(0, 0, 2));
                }
                catch (Exception e)
                {
                    Log("pixivEx", e);
                }

            } 
        }

       


        void OnCollectionViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_collView.SelectedItem != null)
            {

                m_download.Add(((Data)m_collView.SelectedItem).Path);

                m_collView.SelectedItem = null;
            }
        }
    }
}