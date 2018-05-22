using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using Prism.Commands;
using PropertyChanged;

namespace FaceRecognition
{
    [AddINotifyPropertyChangedInterface]
    public partial class MainWindow : Window
    {
        #region Properties
        public ICommand TrainCommand { get; set; }
        public ICommand IdentifyCommand { get; set; }
        public string CameronDiazPath { get; set; } = $@"c:\dev\talks\visiontalk\images\facetraining\CameronDiaz";
        public string TomCruisePath { get; set; } = $@"c:\dev\talks\visiontalk\images\facetraining\TomCruise";
        public string Results { get; set; }
        public FaceServiceClient FaceServiceClient { get; set; }

        private Person CameronDiaz { get; set; }
        private Person TomCruise { get; set; }
        private string GroupId { get; set; } = Guid.NewGuid().ToString();
        private const string SubscriptionKey = "2c670db3c8914967ad6d8aa65d01fa24";
        private const string ServiceEndpoint = "https://westcentralus.api.cognitive.microsoft.com/face/v1.0";
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            FaceServiceClient = new FaceServiceClient(SubscriptionKey, ServiceEndpoint);
            TrainCommand = new DelegateCommand(ProcessImages);
            IdentifyCommand = new DelegateCommand(IdentifyFace);
        }

        private async void IdentifyFace()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".jpg",
                Filter = "Image files(*.jpg, *.png, *.bmp, *.gif) | *.jpg; *.png; *.bmp; *.gif"
            };
            dlg.ShowDialog();

            var pickedImagePath = dlg.FileName;

            using (var fStream = File.OpenRead(pickedImagePath))
            {
                var faces = await FaceServiceClient.DetectAsync(fStream);
                var identifyResult = await FaceServiceClient.IdentifyAsync(faces.Select(ff => ff.FaceId).ToArray(), GroupId);

                var hasMatch = identifyResult.Length > 0 && identifyResult[0].Candidates.Length > 0;
                if (hasMatch)
                {
                    var person = (identifyResult[0].Candidates[0].PersonId == TomCruise.PersonId) ? TomCruise : CameronDiaz;
                    Results = $"{identifyResult[0].Candidates[0].Confidence:P1} confident that this is '{person.Name}'";
                }
                else
                {
                    Results = "Could not identify a match";
                }
            }
        }

        private async Task AddImages(Person person, string imagesPath)
        {
            var imageList = new List<string>(Directory.GetFiles(imagesPath));

            foreach (var imgPath in imageList)
            {
                using (var fStream = File.OpenRead(imgPath))
                {
                    await FaceServiceClient.AddPersonFaceInLargePersonGroupAsync(GroupId, person.PersonId, fStream, imgPath);
                }
            }
         }

        private async void ProcessImages()
        {
            await SetupFaceGroup();

            Dispatcher.Invoke(() => { Results = "Adding Cameron Diaz images..."; });
            CameronDiaz = new Person {Name = "Cameron Diaz"};
            CameronDiaz.PersonId = (await FaceServiceClient.CreatePersonInLargePersonGroupAsync(GroupId, CameronDiaz.Name)).PersonId;
            await AddImages(CameronDiaz, CameronDiazPath);

            Dispatcher.Invoke(() => { Results = "Adding Tom Cruise images..."; });
            TomCruise = new Person {Name = "Tom Cruise"};
            TomCruise.PersonId = (await FaceServiceClient.CreatePersonInLargePersonGroupAsync(GroupId, TomCruise.Name)).PersonId;
            await AddImages(TomCruise, TomCruisePath);

            await TrainFaces();
        }

        private async Task TrainFaces()
        {
            Dispatcher.Invoke(() => { Results = "Training..."; });
            await FaceServiceClient.TrainLargePersonGroupAsync(GroupId);

            // Wait until train completed
            while (true)
            {
                await Task.Delay(1000);
                var status = await FaceServiceClient.GetLargePersonGroupTrainingStatusAsync(GroupId);
                if (status.Status != Status.Running)
                {
                    break;
                }
            }
            Dispatcher.Invoke(() => { Results = "Training Complete."; });
        }

        private async Task SetupFaceGroup()
        {
            var exists = true;
            try
            {
                await FaceServiceClient.GetLargePersonGroupAsync(this.GroupId);
            }
            catch (FaceAPIException ex)
            {
                if (ex.ErrorCode != "LargePersonGroupNotFound")
                {
                    throw;
                }
                else
                {
                    exists = false;
                }
            }

            if (exists)
            {
                await FaceServiceClient.DeleteLargePersonGroupAsync(this.GroupId);
                GroupId = Guid.NewGuid().ToString();
            }

            await FaceServiceClient.CreateLargePersonGroupAsync(this.GroupId, this.GroupId);
        }
    }
}

