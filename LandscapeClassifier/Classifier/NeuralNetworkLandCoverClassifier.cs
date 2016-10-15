﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.ML;
using Emgu.CV.ML.MlEnum;
using Emgu.CV.ML.Structure;
using Emgu.CV.Structure;
using LandscapeClassifier.Extensions;
using LandscapeClassifier.Model;
using LandscapeClassifier.ViewModel;

namespace LandscapeClassifier.Classifier
{
    // TODO see https://github.com/arnaudgelas/OpenCVExamples/blob/master/NeuralNetwork/NeuralNetwork.cpp
    public class NeuralNetworkLandCoverClassifier : ILandCoverClassifier
    {
        private ANN_MLP mlp;

        private const int FeaturesPerVector = 5;
        private readonly int NumClasses;

        public NeuralNetworkLandCoverClassifier()
        {
            NumClasses = Enum.GetValues(typeof(LandcoverType)).Length;
        }

        public void Train(List<ClassifiedFeatureVector> samples)
        {

            mlp = new ANN_MLP();

            var layerSizes = new Matrix<int>(new[] {FeaturesPerVector, 10, 5, NumClasses});
            mlp.SetLayerSizes(layerSizes);
            mlp.SetActivationFunction(ANN_MLP.AnnMlpActivationFunction.SigmoidSym, 1, 1);
            mlp.TermCriteria = new MCvTermCriteria(50, 0.000001d);
            mlp.SetTrainMethod(ANN_MLP.AnnMlpTrainMethod.Backprop, 0.1f, 0.1f);
            

            Matrix<float> trainData = new Matrix<float>(samples.Count, FeaturesPerVector);
            Matrix<float> trainClasses = new Matrix<float>(samples.Count, NumClasses);

            trainClasses.SetZero();

            for (var featureIndex = 0; featureIndex < samples.Count; ++featureIndex)
            {
                var classifiedFeature = samples[featureIndex];

                trainClasses[featureIndex, (int)classifiedFeature.Type] = 1;

                trainData[featureIndex, 0] = classifiedFeature.FeatureVector.Altitude;
                trainData[featureIndex, 1] = classifiedFeature.FeatureVector.Color.GetLuminance();
                trainData[featureIndex, 2] = classifiedFeature.FeatureVector.AverageNeighbourhoodColor.GetLuminance();
                trainData[featureIndex, 3] = classifiedFeature.FeatureVector.Aspect;
                trainData[featureIndex, 4] = classifiedFeature.FeatureVector.Slope;
            }

            using (TrainData data = new TrainData(trainData, DataLayoutType.RowSample, trainClasses))
            {
                ActivationFunctionHardFix(mlp);

                mlp.Train(data);
                   
            }
        }

        public LandcoverType Predict(FeatureVector feature)
        {
            Matrix<float> sampleMat = new Matrix<float>(1, FeaturesPerVector)
            {
                Data =
                {
                    [0, 0] = feature.Altitude,
                    [0, 1] = feature.Color.GetLuminance(),
                    [0, 2] = feature.AverageNeighbourhoodColor.GetLuminance(),
                    [0, 3] = feature.Aspect,
                    [0, 4] = feature.Slope
                }
            };

            Matrix<float> responseMatrix = new Matrix<float>(1, NumClasses);

            mlp.Predict(sampleMat, responseMatrix);

            var maxClass = 0;
            float maxClassValue = responseMatrix.Data[0, 0];
            for (var currenctClass = 1; currenctClass < NumClasses; ++currenctClass)
            {
                if (responseMatrix.Data[0, currenctClass] > maxClassValue)
                {
                    maxClassValue = responseMatrix.Data[0, currenctClass];
                    maxClass = currenctClass;
                }
            }

            return (LandcoverType)maxClass;
        }

        // fix for min/max values as described in http://www.grigaitis.eu/?p=1105
        private void ActivationFunctionHardFix(ANN_MLP network)
        {
            string tmpFile = "tmp.xml";

            // Save temp weights to file for correction before training
            mlp.Save(tmpFile);

            StreamReader reader = new StreamReader(tmpFile);
            string configContent = reader.ReadToEnd();
            reader.Close();

            configContent = configContent.Replace("<min_val>0.", "<min_val>0");
            configContent = configContent.Replace("<max_val>0.", "<max_val>1");
            configContent = configContent.Replace("<min_val1>0.", "<min_val1>0");
            configContent = configContent.Replace("<max_val1>0.", "<max_val1>1");

            StreamWriter writer = new StreamWriter(tmpFile, false);
            writer.Write(configContent);
            writer.Close();

            // Read Fixed values for training
            mlp.Read(new FileStorage("tmp.xml", FileStorage.Mode.Read).GetFirstTopLevelNode());
        }

        public BitmapSource Predict(FeatureVector[,] features)
        {
            var dimensionY = features.GetLength(0);
            var dimensionX = features.GetLength(1);

            Matrix<float> sampleMat = new Matrix<float>(dimensionX * dimensionY, FeaturesPerVector);
            for (var y = 0; y < dimensionY; ++y)
            {
                for (var x = 0; x < dimensionX; ++x)
                {
                    var feature = features[y, x];
                    var featureIndex = y * dimensionX + x;
                    sampleMat.Data[featureIndex, 0] = feature.Altitude;
                    sampleMat.Data[featureIndex, 1] = feature.Color.GetLuminance();
                    sampleMat.Data[featureIndex, 2] = feature.AverageNeighbourhoodColor.GetLuminance();
                    sampleMat.Data[featureIndex, 3] = feature.Aspect;
                    sampleMat.Data[featureIndex, 4] = feature.Slope;
                }
            }

            var dpi = 96d;
            var width = dimensionX;
            var height = dimensionY;

            var stride = width * 4; // 4 bytes per pixel
            var pixelData = new byte[stride * height];

            for (var row = 0; row < sampleMat.Rows; row++)
            {
                // Predict
                Matrix<float> responseMatrix = new Matrix<float>(1, NumClasses);

                mlp.Predict(sampleMat, responseMatrix);

                var maxClass = 0;
                float maxClassValue = responseMatrix.Data[0, 0];
                for (var currenctClass = 1; currenctClass < NumClasses; ++currenctClass)
                {
                    if (responseMatrix.Data[0, currenctClass] > maxClassValue)
                    {
                        maxClassValue = responseMatrix.Data[0, currenctClass];
                        maxClass = currenctClass;
                    }
                }

                var prediction = maxClass;

                var y = row / dimensionX;
                var x = row % dimensionX;
                var landCoverType = (LandcoverType)prediction;
                var color = landCoverType.GetColor();

                pixelData[row * 4 + 0] = color.B;
                pixelData[row * 4 + 1] = color.G;
                pixelData[row * 4 + 2] = color.R;
                pixelData[row * 4 + 3] = color.A;
            }

            var predictionBitmapSource = BitmapSource.Create(dimensionX, dimensionY, dpi, dpi, PixelFormats.Bgra32,
                null, pixelData, stride);

            return predictionBitmapSource;
        }

    }
}