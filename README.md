# FitTracker

A fitness tracking mobile app built with .NET 9. Users can manage their workouts, exercises, and sets — with full authentication and per-user data isolation.

---

## Overview

FitTracker consists of three parts:

- **REST API** — ASP.NET Core Web API, handles all business logic and data
- **Mobile App** — .NET MAUI (Android & iOS), the primary client
- **Web UI** — ASP.NET Core MVC, secondary client (Dockerized)

---

## Features

**Authentication**
- Register / Login with JWT-based authentication
- ASP.NET Core Identity integration
- Per-user data isolation — each user only sees their own workouts

**Workout Management**
- Create, update, delete workouts
- Assign workout type (Upper/Lower, Push/Pull/Legs, etc.)
- Set duration and location (Gym, Home, Park)

**Exercise Management**
- Add exercises to workouts
- Assign intensity level (Low, Medium, High)
- Add optional notes

**Exercise Sets**
- Add and delete sets per exercise
- Track reps and weight (kg) for each set
- Sets display in order

---

## Tech Stack

**Backend**
- ASP.NET Core Web API (.NET 9)
- Entity Framework Core — Code First Migrations
- ASP.NET Core Identity + JWT
- AutoMapper
- FluentValidation
- Azure SQL Database

**Mobile**
- .NET MAUI (Android & iOS)
- MVVM pattern with CommunityToolkit.Mvvm
- HttpClient for API consumption
- SecureStorage for token persistence

**Web UI**
- ASP.NET Core MVC
- Razor views
- Dockerized

**Infrastructure**
- API hosted on Render
- Database on Azure SQL
- Docker

---

## API Endpoints

Base URL: `https://fittracker-stqv.onrender.com`

---

## Project Structure

```
FitTracker/
├── FitTrackr.API/          # REST API
├── FitTrackr.MAUI/         # Mobile app
└── FitTrackr.MVC/          # Web UI
```

---

## Running Locally

**API**
```bash
cd FitTrackr.API
dotnet run
```

**MAUI** — open in Visual Studio, set MAUI project as startup, run on Android emulator or device.
