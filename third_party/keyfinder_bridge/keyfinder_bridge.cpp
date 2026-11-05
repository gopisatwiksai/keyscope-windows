#include "keyfinder.h"
#include "workspace.h"

struct Session {
  explicit Session(unsigned int rate) : sampleRate(rate) { }
  unsigned int sampleRate;
  KeyFinder::KeyFinder finder;
  KeyFinder::Workspace workspace;
};

extern "C" {
  __declspec(dllexport) void* KF_Create(unsigned int sampleRate) {
    try { return new Session(sampleRate); }
    catch (...) { return 0; }
  }

  __declspec(dllexport) void KF_Destroy(void* handle) {
    delete static_cast<Session*>(handle);
  }

  __declspec(dllexport) int KF_AddMonoFloat(void* handle, const float* samples, int count) {
    if (!handle || !samples || count <= 0) return -1;
    try {
      Session* session = static_cast<Session*>(handle);
      KeyFinder::AudioData audio;
      audio.setChannels(1);
      audio.setFrameRate(session->sampleRate);
      audio.addToFrameCount(static_cast<unsigned int>(count));
      for (int i = 0; i < count; ++i) audio.setSample(static_cast<unsigned int>(i), samples[i]);
      session->finder.progressiveChromagram(audio, session->workspace);
      return 0;
    }
    catch (...) { return -2; }
  }

  __declspec(dllexport) int KF_GetKey(void* handle) {
    if (!handle) return -1;
    try {
      Session* session = static_cast<Session*>(handle);
      if (session->workspace.chromagram == 0 || session->workspace.chromagram->getHops() == 0) return 24;
      return static_cast<int>(session->finder.keyOfChromagram(session->workspace));
    }
    catch (...) { return -2; }
  }
}
