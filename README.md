# Atlas Hub

Atlas Hub, Windows masaüstü için geliştirilen modern bir **IPTV/OTT uygulamasıdır**.  
Proje **WPF + MVVM** mimarisi üzerine kuruludur ve **Netflix-benzeri koyu temalı** bir kullanıcı deneyimi hedefler.

> Durum: **Aktif geliştirme (Alpha / Sprint 3 stabilizasyon aşaması)**

---

## Özellikler (Mevcut)

### ✅ Çekirdek Altyapı
- **WPF (.NET 8/9) + MVVM** mimarisi
- **ServiceCollection tabanlı DI** (Generic Host kullanılmıyor)
- Modüler servis yapısı (`Services / ViewModels / Views / Models`)

### ✅ Profil Sistemi
- Profil oluşturma / seçme / silme
- Profil bazlı oturum akışı
- Netflix benzeri “Who’s Watching?” başlangıç ekranı

### ✅ Provider (Kaynak) Yönetimi
- **M3U provider** ekleme
- Provider bazlı **HTTP yapılandırma** (User-Agent, Referer, Timeout)
- Profil bazlı provider **enable/disable**
- Provider katalog yenileme ve saklama (JSON snapshot)

### ✅ Live TV Deneyimi
- 3 panelli Live TV ekranı:
  - Kategoriler
  - Kanal listesi
  - Oynatıcı + program bilgileri
- Kanal arama / kategori arama
- Logo cache altyapısı

### ✅ EPG (XMLTV) Altyapısı
- XMLTV indirme / parse etme
- Provider bazlı EPG repository
- **Now / Next** program bilgisi
- **Timeline (program akışı)**
- M3U header üzerinden **EPG auto-discovery** (`x-tvg-url`, `url-tvg`)
- Kanal eşleme (tvg-id / ad normalize / fuzzy matching)
- Çoklu EPG kaynaklarında duplicate azaltma için iyileştirilmiş yaklaşım

### ✅ Oynatıcı
- **LibVLCSharp (WPF VideoView)** tabanlı oynatma
- Temel oynatma kontrolleri
- Gömülü video alanı (desktop TV UX odaklı)

---

## Mevcut Durum (Sprint Özeti)

Proje şu anda **ileri seviye prototip / alpha** aşamasındadır.

Özellikle:
- Live TV + EPG pipeline çalışır seviyeye getirildi
- Timeline + Program Detail Card UX önemli ölçüde geliştirildi
- Sağ panel düzeni (Video + Program Detay + Timeline + Sticky Kontroller) netleşti

### Aktif Çalışılan Alanlar (Sprint 3 Stabilizasyon)
- Timeline kart seçimi sonrası **scroll / fokus hizalama**
- Timeline kart hizalama / taşma / görünürlük iyileştirmeleri
- Video alanının her durumda güvenilir “fit” davranışı
- Genel UI polish ve regresyon kontrolü

---

## Ekran Yapısı (Live TV)

- **Sol Panel:** Kategori listesi + kategori arama
- **Orta Panel:** Kanal listesi + kanal arama
- **Sağ Panel:**
  - Video Player
  - Program Detail Card
  - Yatay EPG Timeline
  - Sticky Player Controls

---

## Teknoloji Yığını

- **.NET 8 (WPF)**
- **C#**
- **CommunityToolkit.Mvvm**
- **LibVLCSharp / LibVLCSharp.WPF**
- **Microsoft.Extensions.DependencyInjection**
- JSON tabanlı kalıcılık (AppData altında)
- XMLTV parsing (streaming / XmlReader tabanlı)

---

## Kurulum (Geliştirici)

### Gereksinimler
- Windows 10/11
- .NET SDK 8.x (veya proje hedef sürümüne uygun SDK)
- Visual Studio 2022 (WPF geliştirme araçlarıyla)

### Ek Not (LibVLC)
Proje **LibVLCSharp** kullanır. Geliştirme/çalıştırma ortamında LibVLC native bileşenlerinin doğru şekilde yüklü/erişilebilir olması gerekir.

> Paketler projede tanımlıdır; ancak bazı ortamlarda VLC native runtime erişimi ayrıca doğrulanmalıdır.

### Çalıştırma
```bash
git clone <repo-url>
cd AtlasHub
dotnet restore
dotnet build
dotnet run --project AtlasHub
