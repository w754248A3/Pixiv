using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Pixiv
{

    public sealed class CrawlingSettingPageInfo
    {

    }


    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class CrawlingSettingPage : ContentPage
    {

        Crawling m_crawling;

        public CrawlingSettingPage()
        {
            InitializeComponent();


            InitInputView();

            MainThread.BeginInvokeOnMainThread(() => InitViewText());
        }

        void InitInputView()
        {
            InitCrawlingMoedValue();

            m_max_ex_value.Text = InputData.CrawlingMaxExCount;

            m_end_id_value.Text = InputData.CrawlingEndId.ToString();

            m_task_count_value.Text = InputData.TaskCount.ToString();

            m_start_id_value.Text = InputData.CrawlingStartId.ToString();


        }

        async Task InitViewText()
        {
            while (true)
            {
                var crawling = m_crawling;

                if (crawling is null)
                {

                }
                else
                {
                    m_messageView.Text = crawling.Message;

                    InputData.CrawlingStartId = crawling.Id;
                }

                await Task.Delay(new TimeSpan(0, 0, ConstInfo.MESSAGE_FLUSH_TIMESPAN));
            }
        }


        static Crawling.Mode GetCrawlingMoedValue(string s)
        {
            return (Crawling.Mode)Enum.Parse(typeof(Crawling.Mode), s);

        }


        bool CreateInput()
        {
            try
            {
                InputData.CrawlingMaxExCount = m_max_ex_value.Text;

                InputData.CrawlingStartId = InputData.AsNumber(m_start_id_value.Text, 0);

                InputData.CrawlingEndId = m_end_id_value.Text;

                InputData.TaskCount = InputData.AsNumber(m_task_count_value.Text, 1);

                InputData.Count = ConstInfo.PIXIVDATA_PRELOAD_COUNT;

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }


        void InitCrawlingMoedValue()
        {
            var vs = Enum.GetNames(typeof(Crawling.Mode));

            m_crawling_mode_picker.ItemsSource = vs;

            m_crawling_mode_picker.SelectedIndex = 0;
        }

        private void OnStartCrawling(object sender, EventArgs e)
        {


            if (CreateInput())
            {
                m_start_button.IsEnabled = false;

                m_stop_button.IsEnabled = true;

                int id = InputData.CrawlingStartId;

                int? endId = InputData.CreateNullInt32(InputData.CrawlingEndId);

                int? maxExCount = InputData.CreateNullInt32(InputData.CrawlingMaxExCount);

                int taskCount = InputData.TaskCount;

                var moed = GetCrawlingMoedValue(m_crawling_mode_picker.SelectedItem.ToString());

                m_crawling = Crawling.Start(moed, maxExCount, id, endId, taskCount, new TimeSpan(0, 0, ConstInfo.CRAWLING_TIMEOUT));

            }
            else
            {
                DisplayAlert("错误", "必须输入参数", "确定");
            }
        }

        private void OnStopCrawling(object sender, EventArgs e)
        {
            m_stop_button.IsEnabled = false;

            m_start_button.IsEnabled = true;

            m_crawling.CompleteAdding();
        }



        void OnSetMinId(object sender, EventArgs e)
        {
            DataBase.GetMinItemId().ContinueWith((t) =>
            {
                int n = t.Result;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    m_end_id_value.Text = n.ToString();
                });
            });
        }


        void OnSetMaxId(object sender, EventArgs e)
        {
            Task tt = DataBase.GetMaxItemId().ContinueWith((t) =>
            {
                int n = t.Result;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    m_start_id_value.Text = n.ToString();
    
                });
            });

            Log.Write("setlastid", tt);
        }

    }
}