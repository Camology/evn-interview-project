Simple application that loads a set of VINs via provided CSV, hits the NHTSA API for more information (model, year, make, etc), and logs to a local sqlite db.
If errors are returned these are logged as their own entries that you can correct as well. 

To run: 

move to the inner Vehicle-Data/Vehicle-Data folder and run 

```docker-compose up --build```

or if you don't have docker, building from terminal should work like such: 

```dotnet build && dotnet run```

Then navigate to http://localhost:8080/ for the home landing page, or http://localhost:8080/swagger/index.html to read the swaggerdoc 
