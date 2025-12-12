# 🎵 MusicPlayerApp (WPF .NET 8 + ManagedBass)

MusicPlayerApp adalah aplikasi pemutar musik desktop berbasis **WPF (.NET 8)**  
dengan dukungan audio menggunakan **ManagedBass**, pembacaan metadata via **TagLib#**,  
serta fitur sinkronisasi lagu dari folder lokal.

Aplikasi ini dibuat sebagai proyek mata kuliah **Pemrograman Berbasis Objek (PBO)**.

---

## 📥 1. Clone Repository ke Local

Pastikan kamu sudah menginstall **Git**.

```sh
git clone https://github.com/USERNAME/MusicPlayerApp.git
cd MusicPlayerApp

📦 2. Restore Dependencies (NuGet)

Project ini menggunakan beberapa library:

    ManagedBass

    TagLibSharp

    SQLite-net-pcl

    WindowsAPICodePack-Shell

Jika belum ter-restore otomatis oleh Visual Studio, jalankan:

dotnet restore

🎧 3. Wajib Download bass.dll (Native Audio Engine)

ManagedBass membutuhkan file native bernama bass.dll agar pemutaran audio dapat berjalan.

Silakan download dari link resmi berikut:

👉 BASS 2.4 for Windows
https://www.un4seen.com/download.php?bass24

Setelah selesai download, ekstrak lalu ambil file:

bass24\x64\bass.dll

📂 4. Tempatkan bass.dll di folder output aplikasi

Copy file bass.dll ke:

MusicPlayerApp/bin/Debug/net8.0-windows/

atau jika menggunakan mode release:

MusicPlayerApp/bin/Release/net8.0-windows/

⚠️ Tanpa file ini aplikasi akan crash saat memutar lagu.