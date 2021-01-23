using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Pixiv
{
    public partial class App : Application
    {
        public App(string basePath)
        {
            InitializeComponent();

            MainPage = new MainPage(basePath);
        }

        protected override void OnStart()
        {
        }

        protected override void OnSleep()
        {
        }

        protected override void OnResume()
        {
        }
    }
}
