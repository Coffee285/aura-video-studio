# Project Management System - Setup Guide

## Quick Start

This guide will help you set up and use the new project management system in Aura Video Studio.

## Prerequisites

- .NET 8.0 SDK
- Node.js 20+ (for frontend)
- SQLite (included with .NET)

## Backend Setup

### 1. Apply Database Migration

```bash
cd Aura.Api
dotnet ef database update
```

This will create/update the database with all necessary tables:
- ProjectStates
- ProjectVersions
- Templates
- SceneStates
- AssetStates
- RenderCheckpoints
- ContentBlobs

### 2. Verify Service Registration

Ensure services are registered in `Aura.Api/Program.cs`:

```csharp
// Add these services if not already present
builder.Services.AddScoped<ProjectManagementService>();
builder.Services.AddScoped<TemplateManagementService>();
builder.Services.AddScoped<ProjectExportImportService>();
builder.Services.AddScoped<ProjectStateRepository>();
builder.Services.AddScoped<ProjectVersionRepository>();
```

### 3. Seed System Templates

On first run, seed the system templates:

```bash
curl -X POST http://localhost:5000/api/template-management/templates/seed
```

Or through the API once the backend is running.

### 4. Start the Backend

```bash
cd Aura.Api
dotnet run
```

The API will be available at `http://localhost:5000` (or configured port).

## Frontend Setup

### 1. Install Dependencies

```bash
cd Aura.Web
npm install
```

### 2. Configure API URL

Verify the API URL in your environment configuration (`.env` or similar):

```env
VITE_API_URL=http://localhost:5000
```

### 3. Start the Frontend

```bash
cd Aura.Web
npm run dev
```

The web app will be available at `http://localhost:5173` (Vite default).

## Accessing the Project Management Interface

### Navigation

1. Open the web app: `http://localhost:5173`
2. Navigate to **Projects** in the main menu
3. You'll see the project management interface

### Available Routes

- `/projects` - Main projects list (grid/list view)
- `/projects/:id` - Project details view
- `/projects/new` - Create new project (via dialog)
- `/templates` - Browse templates (via dialog)

## Using the System

### Creating Your First Project

1. Click **"New Project"** button
2. Fill in:
   - Project Name (required)
   - Description (optional)
   - Category (optional)
   - Tags (comma-separated, optional)
3. Click **"Create Project"**

### Creating from Template

1. Click **"From Template"** button
2. Browse available templates:
   - Product Demo
   - Tutorial Video
   - Social Media Ad
   - Story Narrative
3. Select a template
4. Enter project name
5. Click **"Create Project"**

### Managing Projects

#### Grid View
- Visual cards with thumbnails
- Status badges
- Quick actions menu
- Bulk selection checkboxes

#### List View
- Compact table view
- Sortable columns
- Detailed information
- Quick actions

#### Search & Filter
- **Search**: Type in search box to filter by title/description
- **Filters**: Click "Filters" button to show filter panel
  - Status filter
  - Category filter
  - Sort by field
  - Sort direction
- **Clear**: Clear filters to show all projects

#### Bulk Actions
- Select multiple projects using checkboxes
- Click **"Delete"** in the bulk actions bar
- Confirm deletion

### Project Details

Click on any project to view:
- **Overview Tab**: Generation parameters, checkpoints, status
- **Scenes Tab**: All video scenes with scripts
- **Assets Tab**: All project files and assets
- **Version History Tab**: Complete version history

### Export & Import

#### Export Project
1. Open project details
2. Click **"Export"** button
3. Choose format:
   - JSON only (lightweight)
   - Package with assets (ZIP)
4. File downloads automatically

#### Import Project
1. Go to Projects list
2. Click **"Import"** button (if added to UI)
3. Select `.aura.json` or `.aura.zip` file
4. Project is imported with new ID

## API Usage Examples

### cURL Examples

#### Get All Projects
```bash
curl http://localhost:5000/api/project-management/projects
```

#### Get Projects with Filters
```bash
curl "http://localhost:5000/api/project-management/projects?status=Completed&category=Tutorial&page=1&pageSize=20"
```

#### Create Project
```bash
curl -X POST http://localhost:5000/api/project-management/projects \
  -H "Content-Type: application/json" \
  -d '{
    "title": "My Video",
    "description": "A tutorial video",
    "category": "Tutorial",
    "tags": ["education", "how-to"]
  }'
```

#### Create from Template
```bash
curl -X POST http://localhost:5000/api/template-management/templates/template-tutorial/create-project \
  -H "Content-Type: application/json" \
  -d '{
    "projectName": "My Tutorial"
  }'
```

#### Export Project
```bash
curl -X POST http://localhost:5000/api/project-management/projects/{projectId}/export \
  -o project.aura.json
```

### JavaScript/TypeScript Examples

```typescript
import { projectManagementApi } from './api/projectManagement';

// Create project
const project = await projectManagementApi.createProject({
  title: "My Project",
  description: "Description",
  category: "Tutorial",
  tags: ["tag1", "tag2"]
});

// Get projects with filters
const result = await projectManagementApi.getProjects({
  search: "tutorial",
  status: "Completed",
  page: 1,
  pageSize: 20
});

// Get project details
const details = await projectManagementApi.getProject(projectId);

// Update project
await projectManagementApi.updateProject(projectId, {
  title: "Updated Title",
  status: "Completed"
});

// Duplicate project
const duplicate = await projectManagementApi.duplicateProject(projectId);

// Delete project
await projectManagementApi.deleteProject(projectId);

// Bulk delete
await projectManagementApi.bulkDeleteProjects([id1, id2, id3]);

// Get templates
const templates = await projectManagementApi.getTemplates();

// Create from template
const project = await projectManagementApi.createProjectFromTemplate(
  templateId,
  "Project Name"
);
```

## Database Management

### View Database

Using SQLite CLI:
```bash
cd Aura.Api
sqlite3 Aura.db
```

Useful commands:
```sql
-- List all tables
.tables

-- View ProjectStates
SELECT * FROM ProjectStates;

-- View Templates
SELECT * FROM Templates;

-- Count projects
SELECT COUNT(*) FROM ProjectStates WHERE IsDeleted = 0;

-- Projects by status
SELECT Status, COUNT(*) 
FROM ProjectStates 
WHERE IsDeleted = 0 
GROUP BY Status;
```

### Backup Database

```bash
# Create backup
cp Aura.Api/Aura.db Aura.Api/Aura.db.backup

# Restore from backup
cp Aura.Api/Aura.db.backup Aura.Api/Aura.db
```

### Reset Database

```bash
cd Aura.Api

# Delete database
rm Aura.db

# Re-run migrations
dotnet ef database update

# Seed templates
curl -X POST http://localhost:5000/api/template-management/templates/seed
```

## Troubleshooting

### Backend Issues

#### "Table does not exist" Error
```bash
cd Aura.Api
dotnet ef database update
```

#### "Service not registered" Error
Check `Program.cs` for service registration. Add:
```csharp
builder.Services.AddScoped<ProjectManagementService>();
builder.Services.AddScoped<TemplateManagementService>();
builder.Services.AddScoped<ProjectExportImportService>();
```

#### Port Already in Use
Change port in `launchSettings.json` or use:
```bash
dotnet run --urls "http://localhost:5001"
```

### Frontend Issues

#### API Connection Failed
1. Check backend is running: `curl http://localhost:5000/health`
2. Verify API URL in frontend config
3. Check CORS settings in backend

#### Components Not Loading
1. Clear browser cache
2. Check console for errors
3. Verify all dependencies installed: `npm install`
4. Rebuild: `npm run build`

#### Search Not Working
1. Check network tab for API calls
2. Verify search parameter in URL
3. Check backend logs for errors

### Database Issues

#### Database Locked
1. Stop all running instances
2. Delete `Aura.db-wal` and `Aura.db-shm` files
3. Restart

#### Slow Queries
1. Check indexes: `PRAGMA index_list(ProjectStates);`
2. Analyze query plan: `EXPLAIN QUERY PLAN SELECT ...`
3. Vacuum database: `VACUUM;`

## Testing

### Backend Unit Tests

```bash
cd Aura.Tests
dotnet test --filter "ProjectManagement"
```

### Frontend Tests

```bash
cd Aura.Web
npm test -- ProjectManagement
```

### Integration Tests

```bash
cd Aura.E2E
dotnet test
```

## Performance Tips

### Database
- Keep database file size reasonable (< 500MB recommended)
- Run `VACUUM` periodically to compact database
- Archive old projects regularly
- Use pagination for large result sets

### Frontend
- Use grid view for better visual experience
- Use list view for better performance with many projects
- Enable caching in React Query
- Debounce search input

### API
- Use appropriate page sizes (10-50 items)
- Filter aggressively to reduce data transfer
- Use projection to return only needed fields
- Enable gzip compression

## Configuration

### Auto-Save Settings

Modify in `ProjectManagementService.cs`:
```csharp
public const int AutoSaveIntervalSeconds = 30; // Default: 30 seconds
```

### Pagination Settings

Default page size: 20
Max page size: 100

Modify in controller if needed:
```csharp
[FromQuery] int pageSize = 20
```

### Template Settings

Add more system templates in `TemplateManagementService.SeedSystemTemplatesAsync()`.

## Next Steps

1. **Explore Templates**: Try creating projects from all 4 system templates
2. **Test Search**: Create multiple projects and test search/filter
3. **Try Bulk Actions**: Select and delete multiple projects
4. **Export/Import**: Test project portability
5. **Check Version History**: Make changes and view versions

## Support

For issues or questions:
1. Check logs in `Aura.Api/logs/`
2. Check browser console for frontend errors
3. Review API response codes and messages
4. Refer to full implementation doc: `PR5_PROJECT_MANAGEMENT_IMPLEMENTATION.md`

## Resources

- [Full Implementation Summary](./PR5_PROJECT_MANAGEMENT_IMPLEMENTATION.md)
- [API Documentation](./api/index.md)
- Database Schema
- Component Library

---

**Version**: 1.0
**Last Updated**: 2025-11-10
**Status**: Production Ready ✅
