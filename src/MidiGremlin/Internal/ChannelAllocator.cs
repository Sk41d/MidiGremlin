using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace MidiGremlin.Internal
{
	internal class ChannelAllocator
	{
		//Music in storage, not ready to be played yet. A queue with last to exit first in index to make modification to end fast
		private readonly List<SingleBeatWithChannel> _storage = new List<SingleBeatWithChannel>();
		//Music in storage that have begun playing but requires more events in the near future
		private readonly List<SimpleMidiMessage> _partialQueue = new List<SimpleMidiMessage>();  
		//What instrument are active on each channel
		private readonly InstrumentType[] _channelInstruments = new InstrumentType[16];
		private const int DRUM_CHANNEL = 9;
		public const double NoMessageTime = double.MaxValue;

		public SimpleMidiMessage GetNext()
		{
			if (Empty)
			{
				throw new NoMoreMusicException();
			}

			if (_storage.Count == 0 || (_partialQueue.Count != 0 && _partialQueue.Last().Timestamp <= _storage.Last().ToneStartTime))
			{
				return _partialQueue.PopLast();
			}
			else
			{
				SingleBeatWithChannel beatWithChannel = _storage.PopLast();

				if (beatWithChannel.InstrumentType == _channelInstruments[beatWithChannel.Channel])
				{
					//X, Y reverse order as list should be in that order
					_partialQueue.MergeInsert(StopMidiMessage(beatWithChannel), CompareSimpleMidi);
					return StartMidiMessage(beatWithChannel);
				}
				else
				{
					_channelInstruments[beatWithChannel.Channel] = beatWithChannel.InstrumentType;
					_partialQueue.MergeInsert(StartMidiMessage(beatWithChannel), CompareSimpleMidi);
					_partialQueue.MergeInsert(StopMidiMessage(beatWithChannel), CompareSimpleMidi);
					return ChangeChannelMidiMessage(beatWithChannel);
				}
			}
		}
		


		public void Add(List<SingleBeat> input)
		{
			InstrumentType[] lastUsedInstruments = new InstrumentType[16];
			Array.Copy(_channelInstruments, lastUsedInstruments, 16);

			double[] finishTimes = CreateFinishTimesArray();
			int storageProgress = _storage.Count;

			for (int index= 0; index < input.Count; index++)
			//{
				
			//}
			//for (int index = input.Count - 1; index >= 0; index--)
			{
				SingleBeat singleBeat = input[index];
				//Find the element in _storage that comes just before us
				while (0 < storageProgress && _storage[storageProgress - 1].ToneStartTime < singleBeat.ToneStartTime)
				{
					storageProgress--;
					int channel = _storage[storageProgress].Channel;
					finishTimes[channel] = Math.Max(_storage[storageProgress].ToneEndTime, finishTimes[channel]);
					lastUsedInstruments[channel] = _storage[storageProgress].InstrumentType;
					
				}

				int usedChannel = lastUsedInstruments.IndexOf(singleBeat.instrumentType);
				if (usedChannel == -1) //If no free channel is found, find one or die
				{
					Func<int, bool> test = singleBeat.instrumentType.IsDrum() ? (Func<int, bool>) (i => i == DRUM_CHANNEL) : (i => i != DRUM_CHANNEL);

					int result = finishTimes.Select((x, y) => new { i = y, val = x})
							.Where(x => test(x.i) && x.val <= singleBeat.ToneStartTime)
							.Select(x => x.i + 1).FirstOrDefault();

					if (result == default(int))
					{
						//won't be free, do whatever
						throw new OutOfChannelsException();
					}
					else
					{
						usedChannel = result - 1;
						lastUsedInstruments[usedChannel] = singleBeat.instrumentType;
					}
				}

				finishTimes[usedChannel] = Math.Max(singleBeat.ToneEndTime, finishTimes[usedChannel]);
				_storage.Insert(storageProgress, singleBeat.WithChannel((byte) usedChannel));
			}
		}



		public double NextTimeStamp
		{
			get
			{
				if (Empty)
				{
					return NoMessageTime;
				}

				if (_storage.Count == 0)
				{
					return _partialQueue[_partialQueue.Count - 1].Timestamp;
				}

				if (_partialQueue.Count == 0)
				{
					return _storage[_storage.Count - 1].ToneStartTime;
				}

				return Math.Min(_storage[_storage.Count - 1].ToneStartTime, _partialQueue[_partialQueue.Count - 1].Timestamp);
			}
		}



		public bool Empty => _partialQueue.Count == 0 && _storage.Count == 0;

		private int CompareSimpleMidi(SimpleMidiMessage lhs, SimpleMidiMessage rhs)
		{
			int first = lhs.Timestamp.CompareTo(rhs.Timestamp);
			if (first != 0)
				return first;

			int lhsType = (lhs.Data & 0xf0) >> 4;
			int rhsType = (rhs.Data & 0xf0) >> 4;

			int second = lhsType.CompareTo(rhsType);
			return second;
		}



        private double[] CreateFinishTimesArray()
		{
			double[] ret = new double[16];

			foreach (SimpleMidiMessage message in _partialQueue)
			{
				ret[message.Channel] = Math.Max(ret[message.Channel], message.Timestamp);
			}

			return ret;
		}



        private int MakeMidiEvent(byte midiEventType, byte channel, byte tone, byte toneVelocity)
		{
			int data = 0;

			data |= channel << 0;
			data |= midiEventType << 4;
			data |= tone << 8;
			data |= toneVelocity << 16;

			return data;
		}



        private SimpleMidiMessage ChangeChannelMidiMessage(SingleBeatWithChannel beatWithChannel)
		{
			return new SimpleMidiMessage(MakeMidiEvent(0xC, beatWithChannel.Channel, (byte)((int)beatWithChannel.InstrumentType & 0x7F), 0), beatWithChannel.ToneStartTime);
		}



        private SimpleMidiMessage StartMidiMessage(SingleBeatWithChannel beatWithChannel)
		{
			return new SimpleMidiMessage(MakeMidiEvent(0x9, beatWithChannel.Channel, beatWithChannel.Tone, beatWithChannel.ToneVelocity), beatWithChannel.ToneStartTime);
		}



        private SimpleMidiMessage StopMidiMessage(SingleBeatWithChannel beatWithChannel)
		{
			return new SimpleMidiMessage(MakeMidiEvent(0x8, beatWithChannel.Channel, beatWithChannel.Tone, beatWithChannel.ToneVelocity), beatWithChannel.ToneEndTime);
		}
	}
}