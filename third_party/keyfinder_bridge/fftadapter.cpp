#include "fftadapter.h"

#include <algorithm>
#include <complex>
#include <vector>

namespace KeyFinder {

  namespace {
    void transform(std::vector<std::complex<double> >& values, bool inverse) {
      const unsigned int n = static_cast<unsigned int>(values.size());
      for (unsigned int i = 1, j = 0; i < n; ++i) {
        unsigned int bit = n >> 1;
        for (; j & bit; bit >>= 1) j ^= bit;
        j ^= bit;
        if (i < j) std::swap(values[i], values[j]);
      }
      for (unsigned int length = 2; length <= n; length <<= 1) {
        const double direction = inverse ? 2.0 : -2.0;
        const std::complex<double> step(std::cos(direction * PI / length),
                                        std::sin(direction * PI / length));
        for (unsigned int start = 0; start < n; start += length) {
          std::complex<double> current(1.0, 0.0);
          const unsigned int half = length >> 1;
          for (unsigned int k = 0; k < half; ++k) {
            const std::complex<double> even = values[start + k];
            const std::complex<double> odd = values[start + k + half] * current;
            values[start + k] = even + odd;
            values[start + k + half] = even - odd;
            current *= step;
          }
        }
      }
    }
  }

  class FftAdapterPrivate {
  public:
    explicit FftAdapterPrivate(unsigned int frameSize) : values(frameSize) { }
    std::vector<std::complex<double> > values;
  };

  FftAdapter::FftAdapter(unsigned int inFrameSize) : frameSize(inFrameSize), priv(new FftAdapterPrivate(inFrameSize)) { }
  FftAdapter::~FftAdapter() { delete priv; }
  unsigned int FftAdapter::getFrameSize() const { return frameSize; }
  void FftAdapter::setInput(unsigned int i, double real) {
    if (i >= frameSize) throw Exception("FFT input index out of bounds");
    if (!std::isfinite(real)) throw Exception("Cannot set FFT sample to NaN");
    priv->values[i] = std::complex<double>(real, 0.0);
  }
  void FftAdapter::execute() { transform(priv->values, false); }
  double FftAdapter::getOutputReal(unsigned int i) const {
    if (i >= frameSize) throw Exception("FFT output index out of bounds");
    return priv->values[i].real();
  }
  double FftAdapter::getOutputImaginary(unsigned int i) const {
    if (i >= frameSize) throw Exception("FFT output index out of bounds");
    return priv->values[i].imag();
  }
  double FftAdapter::getOutputMagnitude(unsigned int i) const {
    if (i >= frameSize) throw Exception("FFT output index out of bounds");
    return std::abs(priv->values[i]);
  }

  class InverseFftAdapterPrivate {
  public:
    explicit InverseFftAdapterPrivate(unsigned int frameSize) : values(frameSize) { }
    std::vector<std::complex<double> > values;
  };

  InverseFftAdapter::InverseFftAdapter(unsigned int inFrameSize) : frameSize(inFrameSize), priv(new InverseFftAdapterPrivate(inFrameSize)) { }
  InverseFftAdapter::~InverseFftAdapter() { delete priv; }
  unsigned int InverseFftAdapter::getFrameSize() const { return frameSize; }
  void InverseFftAdapter::setInput(unsigned int i, double real, double imaginary) {
    if (i >= frameSize) throw Exception("Inverse FFT input index out of bounds");
    if (!std::isfinite(real) || !std::isfinite(imaginary)) throw Exception("Cannot set inverse FFT sample to NaN");
    priv->values[i] = std::complex<double>(real, imaginary);
  }
  void InverseFftAdapter::execute() { transform(priv->values, true); }
  double InverseFftAdapter::getOutput(unsigned int i) const {
    if (i >= frameSize) throw Exception("Inverse FFT output index out of bounds");
    return priv->values[i].real() / frameSize;
  }
}
