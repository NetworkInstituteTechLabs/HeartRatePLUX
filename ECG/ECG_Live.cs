//NOTE
//Add bioplux as a reference
//Set project to x64!
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;
using Multimedia;

using System.IO;
using System.IO.Ports;

namespace ECG
{
    class ECG_Live
    {
        StreamWriter theRawFile;// = new StreamWriter("hrraw001.txt");
        StreamWriter theBPMFile;// = new StreamWriter("bpm001.txt");

        //Timer
        //Next lines just for creating a timer to call reading the device every Interval milliseconds
        Multimedia.Timer myTimer = new Multimedia.Timer(); //Define a new 1ms resolution timer
        int Interval = 10; //Interval in ms for the timer = 100Hz
        int timerCount = 0;

        //Timing
        DateTime startTime;

        //LowPass
        int lpY1, lpY2, lpN;
        int[] lpX = new int[26];
        //
        //HighPass
        int hpY1, hpN;
        int[] hpX = new int[66];
        //
        //Derivative
        int[] x_derv = new int[4];
        //
        //Integral
        int[] x = new int[32];
        int ptr = 0;
        Int64 sum = 0;
        int intWindow = 32; //Window for the integral in samples + 2
        //

        //online detection
        int lastValue = 0; //Lastest sensor value when a change of <deltaThreshold> or more occured in the data
        int lastValueTime = 0; //The time index belonging to <lastValue>
        int deltaValue = 0; //Difference between the previous value (data) en current
        bool goingUp = true; //Direction of the seek
        int time0 = 0; //Time of the previous peak
        int time1 = 0; //Time of the current peak
        int lastValley = 0; //Time of the last valley - NOT used
        int lastPeak = 0; //Time of the last peak
        int prevValue = -1; //Previous data value
        int minTime = 400; //Number of time steps (1/frequency of sampling) to act as minimum time between peaks/beats max beat=150bpm
        int maxTime = 1200; //Number of time steps (1/frequency of sampling) to act as maximum time between peaks/beats min beat=50bpm
        int deltaThreshold = 1; //The difference in data values in a consequtive sequence that is to be ignored (jitter)
        float previousBPM = 0;
        bool bpmWritten = false;
        //

        //PLUX
        Bioplux.Device thePlux; //Init PLUX device
        string pluxID = "00:07:80:79:6F:D0";
        float frequency = 0.01f; //default 100Hz
        //

        public void Init()
        {
            string ppNumber = "0";
            int pp = Convert.ToInt32(ppNumber);
            string rawPath = "Logs/raw";
            string bpmPath = "Logs/bpm";
            string prefix = "";
            if(pp < 10)
            {
                prefix += "00";
            }
            if(pp > 9 && pp < 100)
            {
                prefix += "0"; 
            }
            rawPath += prefix + ppNumber + ".txt";
            bpmPath += prefix + ppNumber + ".txt";

            if(File.Exists(rawPath))
            {
                Console.WriteLine("Log files with this participant number (" + pp + ") already exists. Please try again...");
                return;
            }
            else
            {
                theRawFile = new StreamWriter(rawPath);
                theBPMFile = new StreamWriter(bpmPath);
            }
            InitPlux();
        }
        void InitPlux() //Only runs if we process LIVE
        {
            //Set up vars
            lpY1 = lpY2 = 0;
            lpN = 12;
            hpY1 = 0;
            hpN = 32;
            //

            //Setup timer
            myTimer.Mode = TimerMode.Periodic; //Make sure the timer keeps running after the set period
            myTimer.Period = Interval; //Set the period (when the Event is triggered) to x ms 
            myTimer.Resolution = 1; //Set the resolution of the timer to 1ms
            myTimer.Tick += new System.EventHandler(GetPLUXvalues); //Initialize the EventHandler - every cycle of the Timer the ShowValues function is called

            //Connect to the Plux device using the unique MAC address
            Console.WriteLine("Connecting to plux");
            try
            {
                thePlux = new Bioplux.Device(pluxID);
                Console.WriteLine("Connected to: " + thePlux.GetDescription());
            }
            catch (Bioplux.BPException e)
            {
                Console.WriteLine("BioPLUX could not be found. Please check connections and try again.");

                Console.WriteLine("Press <ENTER> to quit.");
                Console.ReadKey(); //Wait for any key press
                return; //Exit program
            }

            int freq = (int)(1 / frequency);
            //Console.WriteLine(freq);
            thePlux.BeginAcq(freq, 0x0F, 8); //Start the device, using 100Hz sampling, 4 channels (bitmap mask), 8 bit resolution

            startTime = DateTime.Now; //get start time

            myTimer.Start(); //Start timer - Now the ShowValues() function is called every Interval ms
            Console.WriteLine("Press any key when done");
            Console.ReadKey(); //This is to quit it all. Adjust depending on parent code.

            StopHR(); //Close the PLUX connection and stop the Timer
        }

        void GetPLUXvalues(object sender, System.EventArgs args)
        {
            Bioplux.Device.Frame[] frame = new Bioplux.Device.Frame[1]; //Define how many frames or values we want to read, as we call this every few ms, 1 frame is enough
            frame[0] = new Bioplux.Device.Frame(); //Init the frames(s)
            thePlux.GetFrames(1, frame); //Get the one frame with information

            double timeNow = (DateTime.Now - startTime).TotalMilliseconds; //Get time in ms since start

            if (frame[0].an_in[0] != 0)
            {
                //int LPTvalue = ReadArduino();
                //Console.WriteLine(Math.Round(timeNow) + ":" + frame[0].an_in[0] + ":" + frame[0].an_in[1] + ":" + LPTvalue);
                theRawFile.WriteLine(Math.Round(timeNow) + "," + frame[0].an_in[0] + "," + frame[0].an_in[1]);
                //timerCount++; //Increase the time count in frequency of the signal processing

                GetECG(Math.Round(timeNow), frame[0].an_in[0]); //Calculate live BPM
            }
        }


        public void GetECG(double timeNow, int data)
        {
            int timeFrame = Convert.ToInt32(timeNow);
            //Console.WriteLine(timeFrame + ":" + data + "  --  ");
            //theOut.WriteLine(timeFrame + "," + data);
            
            data = LowPassFilter(data); //Low-pass and high-pass filter to remove noise from the signal focussing on the frequencies that are associated with heart beat rates
            //Console.Write(data + "  --  ");
            data = HighPassFilter(data);
            //Console.Write(data + "  --  ");
            data = Derivative(data); //Differentiate to retrieve information about the slope of the signal, this enhances the peak-to-peak signal and reduces other signals (eg P and T waves)
            data = Squared(data); //Make the entire signal positive and amplify the amplitude to 
            data = MovingIntegral(data); //Try to solve difficult slopes without a clear QRS complex so the result is 1 single peak for each beat

            int step = 0;
            int time = timeFrame;
            //Console.WriteLine(data + "  --  ");

            if ((data > prevValue && goingUp) || (data < prevValue && !goingUp)) { step = 1; } //Data larger and going Up OR data smaller and going Down?
            if (data < prevValue && goingUp) { step = 2; } //Data smaller and going up? Just had a peak!
            if (data > prevValue && !goingUp) { step = 3; } //Data larger and going down? Just had a valley!

            deltaValue = Math.Abs(data - lastValue); //Difference in data values between current and previous point

            switch (step)
            {
                case 1: //Looking for peak or valley
                    if (deltaValue > deltaThreshold) //If enough difference between data values
                    {
                        lastValue = data; //Remember this data value
                        lastValueTime = time; //Remember this time index
                    }
                    break;
                case 2: //Possible peak found and switch to looking for valley
                    if (deltaValue > deltaThreshold) //If enough difference between data values
                    {
                        goingUp = false; //Switch to looking for valley
                        lastPeak = lastValueTime + ((time - lastValueTime) / 2); // Get the mid-point of the peak
                        time0 = time1; //Shift previous peak time
                        time1 = lastPeak; //Store current peak time
                        int deltaTime = time1 - time0; //Time ticks between peaks (beats)
                        if (deltaTime > minTime && deltaTime < maxTime) //Is the time difference acceptable?
                        {
                            float bpm = 0; //Do BPM calc
                            bpm = 60f * (1f / ((float)deltaTime / 1000f)); //calc bpm
                            //Console.WriteLine(time1 + ":" + bpm.ToString("0.00") + ":" + deltaTime + "," + LPT);
                            theBPMFile.WriteLine(time1 + "," + bpm.ToString("0.00"));
                            previousBPM = bpm;
                            bpmWritten = true;
                        }
                    }
                    break;
                case 3: //Possible valley found and switch to looking for peak
                    if (deltaValue > deltaThreshold) //If enough difference between data values
                    {
                        goingUp = true; //Switch to looking for a peak
                        lastValley = lastValueTime + ((time - lastValueTime) / 2); //Get the mid-point of the valley - NOT used
                    }
                    break;
                default:
                    //Console.WriteLine();
                    break;
            }
            prevValue = data; //Remember this value for next pass

        }

        int TwoPoleRecursive(int data)
        {
            int xnt, xm1, xm2, ynt, ym1, ym2;
            xnt = xm1 = xm2 = ynt = ym1 = ym2 = 0;

            xnt = data;
            ynt = (ym1 + ym1 >> 1 + ym1 >> 2 + ym1 >> 3) + (ym2 >> 1 + ym2 >> 2 + ym2 >> 3 + ym2 >> 5 + ym2 >> 6) + xnt - xm2;

            xm2 = xm1;
            xm1 = xnt;
            xm2 = ym1;
            ym2 = ym1;
            ym1 = ynt;

            return ynt;
        }

        int LowPassFilter(int data)
        {
            int y0;

            lpX[lpN] = lpX[lpN + 13] = data;
            y0 = (lpY1 << 1) - lpY2 + lpX[lpN] - (lpX[lpN + 6] << 1) + lpX[lpN + 12];
            lpY2 = lpY1;
            lpY1 = y0;
            y0 >>= 5;
            if (--lpN < 0) { lpN = 12; }
            return y0;
        }

        int HighPassFilter(int data)
        {
            int y0 = 0;

            hpX[hpN] = hpX[hpN + 33] = data;
            y0 = hpY1 + hpX[hpN] - hpX[hpN + 32];
            hpY1 = y0;
            if (--hpN < 0) { hpN = 32; }
            return (hpX[hpN + 16] - (y0 >> 5));
        }

        int Derivative(int data)
        {
            int y, i;

            y = (data << 1) + x_derv[3] - x_derv[1] - (x_derv[0] << 1);
            y >>= 3;
            for (i = 0; i < 3; i++)
            {
                x_derv[i] = x_derv[i + 1];
            }
            x_derv[3] = data;
            return y;
        }

        int Squared(int data)
        {
            return (data * data);
        }

        int MovingIntegral(int data)
        {
            Int64 ly;
            int y;

            if (++ptr == intWindow) { ptr = 0; }
            sum -= x[ptr];
            sum += data;
            x[ptr] = data;
            ly = sum >> 5;
            if (ly > 32400) { y = 32400; }
            else { y = (int)ly; }
            return y;
        }

        void StopHR() //Stop timer and Plux connection
        {
            myTimer.Stop(); //Stop the timer

            try
            {
                thePlux.EndAcq(); //Stop the Plux device
                thePlux.Dispose();
            }
            catch(Exception e)
            {
                Console.WriteLine(e.Message);
            }

            theRawFile.Close();
            theBPMFile.Close();
        }

    }
}
