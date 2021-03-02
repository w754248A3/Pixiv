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
    public sealed class ViewImageSettingPageInfo
    {
        public ViewImageSettingPageInfo(LoadBigImg loadBigImg)
        {
            LoadBigImg = loadBigImg;
        }

        public LoadBigImg LoadBigImg { get; }


    }

    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ViewImageSettingPage : ContentPage
    {
        ViewImageSettingPageInfo m_info;

        public ViewImageSettingPage(ViewImageSettingPageInfo info)
        {
            InitializeComponent();

            m_info = info;

            Init();
        }

        void Init()
        {
            m_max_id_value.Text = InputData.MaxId;

            m_min_id_value.Text = InputData.MinId;

            m_max_mark_value.Text = InputData.MaxMark;

            m_min_mark_value.Text = InputData.MinMark;

            m_tag_value.Text = InputData.Tag;

            m_not_tag_value.Text = InputData.NotTag;


            m_offset_value.Text = InputData.Offset.ToString();

            m_view_column_value.Text = InputData.ViewColumn.ToString();

            m_add_img_timespan_value.Text = InputData.AddItemTimeSpan.ToString();
        }

        bool SetInit()
        {
            try
            {

                InputData.MaxId = m_max_id_value.Text ?? "";

                InputData.MinId = m_min_id_value.Text ?? "";

                InputData.MaxMark = m_max_mark_value.Text ?? "";

                InputData.MinMark = m_min_mark_value.Text ?? "";

                InputData.Tag = m_tag_value.Text ?? "";

                InputData.NotTag = m_not_tag_value.Text ?? "";

                InputData.Offset = InputData.AsNumber(m_offset_value.Text, 0);

                InputData.ViewColumn = InputData.AsNumber(m_view_column_value.Text, 1);

                InputData.AddItemTimeSpan = InputData.AsNumber(m_add_img_timespan_value.Text, 1);

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private void OnStartViewImag(object sender, EventArgs e)
        {
            if (SetInit())
            {
                var info = new ViewListImagePageInfo(InputData.ViewColumn, m_info.LoadBigImg);

                info.Task.ContinueWith((t) => MainThread.BeginInvokeOnMainThread(() => Init()));

                Navigation.PushModalAsync(new ViewListImagePage(info));


                
            }
            else
            {
                DisplayAlert("错误", "参数问题", "确定");
            }
        }

        void CreateInput(string message,  Label label)
        {
            var info = new InputTextPageInfo(message, label.Text, InputData.GetTagHistry());

            Navigation.PushModalAsync(new InputTextPage(info));

            info.Task.ContinueWith((t) =>
            {
                if (t.IsCanceled)
                {

                }
                else
                {
                    var result = t.Result;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        label.Text = result.Result;

                        InputData.SetTagHistry(result.List);
                    });
                }
            });
        }

        private void OnInputTag(object sender, EventArgs e)
        {
            CreateInput("输入Tag", m_tag_value);
        }

        private void OnInputNotTag(object sender, EventArgs e)
        {
            CreateInput("输入排除Tag", m_not_tag_value);
        }
    }
}