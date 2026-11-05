// MinGW.org's GCC 6 standard library in this build environment has no std::mutex.
// KeyScope calls the embedded analyser from one timer thread, so this compatibility shim is sufficient
// for the local proof build. Production sources should use a current toolchain with real std::mutex.
namespace std {
  class mutex {
  public:
    void lock() { }
    void unlock() { }
  };
}
