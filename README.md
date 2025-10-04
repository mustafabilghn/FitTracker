# FitTracker

[![.NET](https://img.shields.io/badge/.NET-9-blue)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-lightgrey)](LICENSE)

## 🚀 Overview

**FitTracker** is a fitness tracking system built with **.NET 9**, consisting of a **RESTful Web API**, an **ASP.NET Core MVC** client application, and a **.NET MAUI** mobile app.  
It allows users to manage workout programs, exercises, intensity levels, and workout locations — while following clean coding principles and modern ASP.NET Core best practices.  

---

## 🧠 Features

- **Workout Management**  
  - Support for different workout types (Upper/Lower, Push/Pull/Legs)  
  - CRUD operations for workouts  
  - Assign **workout location** (Gym, Home, Park, etc.)  

- **Exercise Management**  
  - CRUD operations for exercises (Bench Press, Squat, Deadlift, etc.)  
  - Assign intensity levels and weights  
  - Link exercises to workouts  

- **Intensity Levels**  
  - Categorize exercises by difficulty (Easy, Medium, Hard, etc.)  

- **Workout Locations**  
  - CRUD operations for workout locations  
  - Associate each workout with a specific location  

- **Authentication & Authorization**  
  - ASP.NET Core Identity integration  
  - JWT-based authentication  
  - Role-based authorization  

- **Advanced API Features**  
  - Filtering, sorting, and pagination  
  - Validation with Data Annotations  
  - AutoMapper for DTO mapping  
  - Repository Pattern and Domain-Driven Design (DDD)  
  - Global error handling with ProblemDetails  
  - Swagger documentation  
  - Postman testing support  

- **Cross-Platform Mobile App (MAUI)**  
  - Native mobile client consuming the API  
  - Workout list & details view  
  - Real-time API integration with `HttpClient`  
  - Designed for both Android and iOS  

---

## 🛠 Tech Stack

**Backend:**  
- ASP.NET Core Web API (.NET 9)  
- Entity Framework Core (Code First Migrations)  
- ASP.NET Core Identity + JWT  
- AutoMapper  
- SQL Server  

**Frontend (Web):**  
- ASP.NET Core MVC  
- HttpClient-based API consumption  
- Razor views with server-side rendering  

**Frontend (Mobile):**  
- .NET MAUI (Android & iOS)  
- MVVM pattern  
- CollectionView-based UI  
- Dependency Injection + HttpClient  

**Tools:**  
- Swagger / Swashbuckle  
- Postman  
- LINQ  
- GitHub for version control  
- Docker  

---

## 🌐 Deployment

- **API Hosting:** Deployed on [Render](https://render.com) for public availability.  
- **Database:** Connected to an **Azure SQL Database**, with credentials managed through Render environment variables.  
- **Mobile App Integration:** The .NET MAUI client consumes the live API endpoint hosted on Render using `HttpClient`.
  
---

## 🔗 Live Demo

The API is live and accessible through the following endpoints (hosted on Render):

- **Workout List:** [https://fittracker-stqv.onrender.com/api/workout](https://fittracker-stqv.onrender.com/api/workout)  
- **Exercise List:** [https://fittracker-stqv.onrender.com/api/exercise](https://fittracker-stqv.onrender.com/api/exercise)  

> Note: Since this is a demo project, some endpoints may have limited functionality or require authentication.

