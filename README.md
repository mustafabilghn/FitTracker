# FitTrackr

[![.NET](https://img.shields.io/badge/.NET-8-blue)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-lightgrey)](LICENSE)

## 🚀 Overview

**FitTrackr** is a fitness tracking system built with **.NET 9**, consisting of a **RESTful Web API** and an **ASP.NET Core MVC** client application.  
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
  - Categorize exercises by difficulty (Easy, Meddium, Hard, etc.)

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

---

## 🛠 Tech Stack

**Backend:**
- ASP.NET Core Web API (.NET 9)
- Entity Framework Core (Code First Migrations)
- ASP.NET Core Identity + JWT
- AutoMapper
- SQL Server

**Frontend (Client):**
- ASP.NET Core MVC
- HttpClient-based API consumption
- Razor views with server-side rendering

**Tools:**
- Swagger / Swashbuckle
- Postman
- LINQ
- GitHub for version control
