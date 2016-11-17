﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Messaging;
using LandscapeClassifier.Classifier;
using LandscapeClassifier.Model;
using LandscapeClassifier.Model.Classification;
using LandscapeClassifier.View.Open;
using LandscapeClassifier.ViewModel.Dialogs;
using MathNet.Numerics.LinearAlgebra;

namespace LandscapeClassifier.ViewModel.MainWindow.Classification
{
    public class ClassifierViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        private bool _areBandsUnscaled;
        private FeaturesViewModel _featuresViewModel;
        private bool _isAllPredicted;
        private bool _isTrained;
        private Point _mouseScreenPoisition;
        private Point _mouseWorldPoisition;

        private bool _previewBandIntensityScale = true;

        private Classifier.Classifier _selectededClassifier = Classifier.Classifier.DecisionTrees;

        private ClassifiedFeatureVectorViewModel _selectedFeatureVector;

        private LandcoverType _selectedLandCoverType;
        private SolidColorBrush _trainingStatusBrush;

        private string _trainingStatusText;

        /// <summary>
        ///     Conversion from screen to world coordinates.
        /// </summary>
        public Matrix<double> ScreenToWorld;

        /// <summary>
        ///     Conversion from world to screen coordinates.
        /// </summary>
        public Matrix<double> WorldToScreen;

        public ClassifierViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            // TODO nono
            ScreenToWorld = Matrix<double>.Build.DenseOfArray(new[,] { { 1, 0, 300000.0 }, { 0, -1, 5090220 }, { 0, 0, 1 } });
            WorldToScreen = ScreenToWorld.Inverse();

            mainWindowViewModel.Layers.CollectionChanged += BandsOnCollectionChanged;

            LandCoverTypesEnumerable = Enum.GetNames(typeof(LandcoverType));
            ClassifiersEnumerable = Enum.GetNames(typeof(Classifier.Classifier));

            FeaturesViewModel = new FeaturesViewModel();

            RemoveAllFeaturesCommand = new RelayCommand(() => FeaturesViewModel.RemoveAllFeatures(),
                () => FeaturesViewModel.HasFeatures());

            RemoveSelectedFeatureVectorCommand = new RelayCommand(RemoveSelectedFeature, CanRemoveSelectedFeature);

            PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(SelectededClassifier))
                {
                    CurrentClassifier = SelectededClassifier.CreateClassifier();
                    MarkClassifierNotTrained();
                }
            };
            SelectededClassifier = Classifier.Classifier.DecisionTrees;
            CurrentClassifier = SelectededClassifier.CreateClassifier();


            FeaturesViewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == FeaturesViewModel.FeatureProperty)
                {
                    MarkClassifierNotTrained();
                    foreach (var bandViewModel in _mainWindowViewModel.Layers)
                    {
                        bandViewModel.CanChangeIsFeature = !FeaturesViewModel.HasFeatures();
                    }
                }
            };

            MarkClassifierNotTrained();

            ExportFeaturesCommand = new RelayCommand(ExportTrainingSet, CanExportTrainingSet);
            ImportFeatureCommand = new RelayCommand(ImportTrainingSet, CanImportTrainingSet);

            TrainCommand = new RelayCommand(Train, CanTrain);
        }

        /// <summary>
        ///     Export command.
        /// </summary>
        public ICommand ExportFeaturesCommand { set; get; }

        /// <summary>
        ///     Train command.
        /// </summary>
        public ICommand TrainCommand { set; get; }

        /// <summary>
        ///     Import features command.
        /// </summary>
        public ICommand ImportFeatureCommand { set; get; }

        /// <summary>
        ///     Remove selected command.
        /// </summary>
        public ICommand RemoveSelectedFeatureVectorCommand { set; get; }

        /// <summary>
        ///     Remove all features command.
        /// </summary>
        public ICommand RemoveAllFeaturesCommand { set; get; }

        /// <summary>
        ///     Classified Features.
        /// </summary>
        public FeaturesViewModel FeaturesViewModel
        {
            get { return _featuresViewModel; }
            set
            {
                _featuresViewModel = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        ///     The projection of the band image.
        /// </summary>
        public string ProjectionName { get; set; }

        /// <summary>
        ///     Whether multiple bands can be visible or not.
        /// </summary>
        public bool MultipleBandsEnabled { get; set; }


        /// <summary>
        ///     Mouse screen position.
        /// </summary>
        public Point MouseScreenPoisition
        {
            get { return _mouseScreenPoisition; }
            set
            {
                _mouseScreenPoisition = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        ///     Mouse world position.
        /// </summary>
        public Point MouseWorldPoisition
        {
            get { return _mouseWorldPoisition; }
            set
            {
                _mouseWorldPoisition = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        ///     The currently selected feature vector.
        /// </summary>
        public ClassifiedFeatureVectorViewModel SelectedFeatureVector
        {
            get { return _selectedFeatureVector; }
            set
            {
                if (value != _selectedFeatureVector)
                {
                    _selectedFeatureVector = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        ///     The current classifier.
        /// </summary>
        public Classifier.Classifier SelectededClassifier
        {
            get { return _selectededClassifier; }
            set
            {
                if (value != _selectededClassifier)
                {
                    _selectededClassifier = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        ///     Status text for the training tab.
        /// </summary>
        public string TrainingStatusText
        {
            get { return _trainingStatusText; }
            set
            {
                if (value != _trainingStatusText)
                {
                    _trainingStatusText = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        ///     Brush used for the training status label.
        /// </summary>
        public SolidColorBrush TrainingStatusBrush
        {
            get { return _trainingStatusBrush; }
            set
            {
                if (value != _trainingStatusBrush)
                {
                    _trainingStatusBrush = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        ///     Returns if all pixels in the prediction tab have been predicted by the classifier.
        /// </summary>
        public bool IsAllPredicted
        {
            get { return _isAllPredicted; }
            set
            {
                if (value != _isAllPredicted)
                {
                    _isAllPredicted = value;
                    RaisePropertyChanged(nameof(IsAllPredicted));
                }
            }
        }

        /// <summary>
        ///     True if the classifier has been trained.
        /// </summary>
        public bool IsTrained
        {
            get { return _isTrained; }
            set
            {
                if (value != _isTrained)
                {
                    _isTrained = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        ///     The current land cover type.
        /// </summary>
        public LandcoverType SelectedLandCoverType
        {
            get { return _selectedLandCoverType; }
            set
            {
                _selectedLandCoverType = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        ///     Whether the preview of the band intensity should scale to min max.
        /// </summary>
        public bool PreviewBandIntensityScale
        {
            get { return _previewBandIntensityScale; }
            set
            {
                _previewBandIntensityScale = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        ///     Possible land cover types.
        /// </summary>
        public IEnumerable<string> LandCoverTypesEnumerable { get; set; }

        /// <summary>
        ///     Possible classifiers.
        /// </summary>
        public IEnumerable<string> ClassifiersEnumerable { get; set; }

        /// <summary>
        ///     The current used classifier.
        /// </summary>
        public ILandCoverClassifier CurrentClassifier { get; private set; }

        private void RemoveSelectedFeature()
        {
            FeaturesViewModel.RemoveFeature(SelectedFeatureVector);
        }

        private bool CanRemoveSelectedFeature()
        {
            return SelectedFeatureVector != null;
        }

        private void BandsOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs changedEvents)
        {
            if (changedEvents.NewItems != null)
            {
                foreach (LayerViewModel bandViewModel in changedEvents.NewItems)
                {
                    bandViewModel.PropertyChanged += BandViewModelOnPropertyChanged;
                }
            }
            if (changedEvents.OldItems != null)
            {
                foreach (LayerViewModel bandViewModel in changedEvents.OldItems)
                {
                    bandViewModel.PropertyChanged -= BandViewModelOnPropertyChanged;
                }
            }
        }

        private void BandViewModelOnPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            if (MultipleBandsEnabled) return;

            var changedLayer = (LayerViewModel)sender;
            if (propertyChangedEventArgs.PropertyName == nameof(LayerViewModel.IsVisible))
            {
                foreach (var bandViewModel in _mainWindowViewModel.Layers)
                {
                    if (bandViewModel == changedLayer) continue;

                    bandViewModel.PropertyChanged -= BandViewModelOnPropertyChanged;
                    bandViewModel.IsVisible = false;
                    bandViewModel.PropertyChanged += BandViewModelOnPropertyChanged;
                }
            }
        }

        private void MarkClassifierNotTrained()
        {
            TrainingStatusText = "Classifier is NOT trained";
            TrainingStatusBrush = new SolidColorBrush(Colors.DarkRed);
            IsTrained = false;
        }

        private void MarkClassifierTrained()
        {
            TrainingStatusText = "Classifier is trained";
            TrainingStatusBrush = new SolidColorBrush(Colors.DarkGreen);
        }

        private void ExportTrainingSet()
        {
            // Create an instance of the open file dialog box.
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Txt Files (.txt)|*.txt",
                FilterIndex = 1
            };

            // Call the ShowDialog method to show the dialog box.
            var userClickedOk = saveFileDialog.ShowDialog();

            // Process input if the user clicked OK.
            if (userClickedOk == DialogResult.OK)
            {
                var csvPath = saveFileDialog.FileName;
                using (var outputStreamWriter = new StreamWriter(csvPath))
                {
                    var layers = _mainWindowViewModel.Layers.Where(b => b.IsFeature).OrderBy(b => b.Name);
                    outputStreamWriter.WriteLine(layers.Count());
                    foreach (var layerViewModel in layers)
                    {
                        outputStreamWriter.WriteLine(layerViewModel.Path);
                        outputStreamWriter.WriteLine(layerViewModel.ContrastEnhanced);
                        outputStreamWriter.WriteLine(layerViewModel.SatelliteType);
                        outputStreamWriter.WriteLine(layerViewModel.IsRed);
                        outputStreamWriter.WriteLine(layerViewModel.IsGreen);
                        outputStreamWriter.WriteLine(layerViewModel.IsBlue);
                        outputStreamWriter.WriteLine(layerViewModel.MinCutPercentage);
                        outputStreamWriter.WriteLine(layerViewModel.MaxCutPercentage);
                    }

                    foreach (var feature in FeaturesViewModel.AllFeaturesView.Select(f => f.ClassifiedFeatureVector))
                    {
                        var featureString = feature.FeatureVector.BandIntensities.Aggregate(feature.Type.ToString(),
                            (accu, intensity) => accu + ";" + intensity);
                        outputStreamWriter.WriteLine(featureString);
                    }
                }
            }
        }

        private bool CanExportTrainingSet()
        {
            return FeaturesViewModel.HasFeatures();
        }

        private void ImportTrainingSet()
        {
            // Create an instance of the open file dialog box.
            var openFileDialog = new OpenFileDialog
            {
                Filter = @"Txt Files (.txt)|*.txt",
                FilterIndex = 1
            };

            // Call the ShowDialog method to show the dialog box.
            var userClickedOk = openFileDialog.ShowDialog();

            // Process input if the user clicked OK.
            if (userClickedOk == DialogResult.OK)
            {
                FeaturesViewModel.RemoveAllFeatures();

                using (var file = new StreamReader(openFileDialog.FileName))
                {
                    int numLayers = int.Parse(file.ReadLine());
                    var layers = new List<CreateLayerViewModel>();
                    for (int i = 0; i < numLayers; ++i)
                    {
                        string path = file.ReadLine();
                        bool contrastEnhancement = bool.Parse(file.ReadLine());
                        SatelliteType satelliteType = (SatelliteType)Enum.Parse(typeof(SatelliteType), file.ReadLine());
                        bool isRed = bool.Parse(file.ReadLine());
                        bool isGreen = bool.Parse(file.ReadLine());
                        bool isBlue = bool.Parse(file.ReadLine());
                        double minCutPercentage = double.Parse(file.ReadLine());
                        double maxCutPercentage = double.Parse(file.ReadLine());
                        layers.Add(new CreateLayerViewModel(path, contrastEnhancement, satelliteType, isRed, isGreen, isBlue, minCutPercentage, maxCutPercentage)); ;
                    }

                    var missingLayers = layers.Where(s => _mainWindowViewModel.Layers.All(b => b.Path != s.Path)).ToList();

                    // Check if some bands have to be added.
                    if (missingLayers.Count > 0)
                    {
                        var dialog = new AddLayersDialog();

                        if (dialog.ShowImportMissingBandsDialog(missingLayers) ==
                            true && dialog.DialogViewModel.Layers.Count > 0)
                        {
                            _mainWindowViewModel.AddBands(dialog.DialogViewModel);
                        }
                        else
                        {
                            // Abort import
                            return;
                        }
                    }

                    string featureLine;

                    while ((featureLine = file.ReadLine()) != null)
                    {
                        string[] feature = featureLine.Split(';');
                        LandcoverType type;
                        Enum.TryParse(feature[0], true, out type);
                        var intensities = feature.Skip(1).Select(ushort.Parse).ToArray();

                        FeaturesViewModel.AddFeature(new ClassifiedFeatureVectorViewModel(new ClassifiedFeatureVector(type,
                            new FeatureVector(intensities))));
                    }
                }




            }
        }

        private bool CanImportTrainingSet()
        {
            return true;
        }

        /// <summary>
        ///     Trains the classifier.
        /// </summary>
        private void Train()
        {
            var classifiedFeatureVectors =
                FeaturesViewModel.AllFeaturesView.Select(f => f.ClassifiedFeatureVector).ToList();
            var bands = _mainWindowViewModel.Layers.Where(b => b.IsFeature).Select(b => b.Name).ToList();

            CurrentClassifier.Train(new ClassificationModel(ProjectionName, bands, classifiedFeatureVectors));
            IsTrained = true;

            MarkClassifierTrained();
        }


        private bool CanTrain()
        {
            return FeaturesViewModel.HasFeatures();
        }
    }
}