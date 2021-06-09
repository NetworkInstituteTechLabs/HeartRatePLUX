# HeartRatePLUX
Calculates Beats-Per-Minute from raw BioSignalsPLUX BVP sensor
</br>
## Remarks
C# console program</br>
</br>
## Usage
Note this is working but rough code. It implements the algorithm of Afonso (1993) to convert live raw heart potential signals to a Beats-Per-Minute value.</br>
It uses a BioSignalPLUX BVP sensor set to 100Hz sampling to collect the raw data.</br>
It uses a simple timer that polls the sensor every 10ms (100Hz).</br>
It stores two log files in the folder <Logs> and the sub-folders <raw> and <bpm>. The log files have fixed names so please, change this if you want.</br>
It uses a fixed sensor MAC address, so change it to suit yours!</br>
</br>
After starting and succesful connecting to the BVP sensor the recording and calculations start until a (any) key is pressed.</br>
