'use client'

import { useState, useEffect } from 'react'
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts'
import * as api from '../lib/api'

export default function RoadmapPage() {
  const [roadmapItems, setRoadmapItems] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [newTask, setNewTask] = useState({ task: '', quarter: 'Q2 2026', status: 'planned', progress_percent: 0 })
  
  useEffect(() => {
    loadRoadmap()
  }, [])
  
  const loadRoadmap = async () => {
    try {
      setLoading(true)
      // For now, we'll use mock data since the backend doesn't have a roadmap endpoint yet
      const mockData = [
        { id: 1, task: 'Implement authentication', quarter: 'Q2 2026', status: 'in_progress', progress_percent: 75 },
        { id: 2, task: 'Add document generation', quarter: 'Q2 2026', status: 'planned', progress_percent: 0 },
        { id: 3, task: 'Deploy to production', quarter: 'Q3 2026', status: 'planned', progress_percent: 0 },
        { id: 4, task: 'Add AI features', quarter: 'Q3 2026', status: 'planned', progress_percent: 0 },
        { id: 5, task: 'User testing', quarter: 'Q3 2026', status: 'planned', progress_percent: 0 }
      ]
      setRoadmapItems(mockData)
    } catch (err) {
      setError('Failed to load roadmap')
      console.error(err)
    } finally {
      setLoading(false)
    }
  }
  
  const handleAddTask = async () => {
    if (newTask.task.trim()) {
      try {
        // For now, we'll just add to local state since the backend doesn't have a roadmap endpoint yet
        const newItem = {
          id: Math.max(...roadmapItems.map(item => item.id), 0) + 1,
          ...newTask
        }
        setRoadmapItems([...roadmapItems, newItem])
        setNewTask({ task: '', quarter: 'Q2 2026', status: 'planned', progress_percent: 0 })
      } catch (err) {
        setError('Failed to add task')
        console.error(err)
      }
    }
  }
  
  const handleUpdateProgress = async (id, progress) => {
    try {
      const updatedProgress = Math.max(0, Math.min(100, progress))
      // For now, we'll just update local state since the backend doesn't have a roadmap endpoint yet
      setRoadmapItems(roadmapItems.map(item => 
        item.id === id ? { ...item, progress_percent: updatedProgress } : item
      ))
    } catch (err) {
      setError('Failed to update progress')
      console.error(err)
    }
  }
  
  const getStatusColor = (status) => {
    switch (status) {
      case 'completed': return 'bg-green-100 text-green-800'
      case 'in_progress': return 'bg-blue-100 text-blue-800'
      case 'planned': return 'bg-gray-100 text-gray-800'
      default: return 'bg-gray-100 text-gray-800'
    }
  }
  
  // Group tasks by quarter for the chart
  const chartData = roadmapItems.reduce((acc, item) => {
    const quarter = item.quarter
    if (!acc[quarter]) {
      acc[quarter] = { quarter, planned: 0, in_progress: 0, completed: 0 }
    }
    acc[quarter][item.status]++
    return acc
  }, {})
  
  const chartDataArray = Object.values(chartData)
  
  if (loading) {
    return (
      <div className="flex justify-center items-center h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-blue-500"></div>
      </div>
    )
  }
  
  if (error) {
    return (
      <div className="flex justify-center items-center h-screen">
        <div className="bg-red-100 border border-red-400 text-red-700 px-4 py-3 rounded relative" role="alert">
          <strong className="font-bold">Error: </strong>
          <span className="block sm:inline">{error}</span>
        </div>
      </div>
    )
  }
  
  return (
    <div className="p-6">
      <div className="mb-8">
        <h1 className="text-3xl font-bold text-gray-900">Development Roadmap</h1>
        <p className="text-gray-600 mt-2">Track progress and plan future development</p>
      </div>
      
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 mb-8">
        <div className="bg-white rounded-lg border border-gray-200 p-6">
          <h3 className="text-lg font-semibold text-gray-900 mb-4">Add New Task</h3>
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Task</label>
              <input
                type="text"
                className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
                value={newTask.task}
                onChange={(e) => setNewTask({...newTask, task: e.target.value})}
                placeholder="Enter task description"
              />
            </div>
            
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Quarter</label>
                <select
                  className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
                  value={newTask.quarter}
                  onChange={(e) => setNewTask({...newTask, quarter: e.target.value})}
                >
                  <option value="Q2 2026">Q2 2026</option>
                  <option value="Q3 2026">Q3 2026</option>
                  <option value="Q4 2026">Q4 2026</option>
                  <option value="Q1 2027">Q1 2027</option>
                </select>
              </div>
              
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Status</label>
                <select
                  className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
                  value={newTask.status}
                  onChange={(e) => setNewTask({...newTask, status: e.target.value})}
                >
                  <option value="planned">Planned</option>
                  <option value="in_progress">In Progress</option>
                  <option value="completed">Completed</option>
                </select>
              </div>
            </div>
            
            <button
              onClick={handleAddTask}
              className="w-full px-4 py-2 text-white bg-blue-600 rounded-md hover:bg-blue-700"
            >
              Add Task
            </button>
          </div>
        </div>
        
        <div className="lg:col-span-2 bg-white rounded-lg border border-gray-200 p-6">
          <h3 className="text-lg font-semibold text-gray-900 mb-4">Progress Overview</h3>
          <div className="h-64">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart
                data={chartDataArray}
                margin={{
                  top: 5,
                  right: 30,
                  left: 20,
                  bottom: 5,
                }}
              >
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="quarter" />
                <YAxis />
                <Tooltip />
                <Legend />
                <Bar dataKey="planned" fill="#94a3b8" name="Planned" />
                <Bar dataKey="in_progress" fill="#3b82f6" name="In Progress" />
                <Bar dataKey="completed" fill="#10b981" name="Completed" />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>
      
      <div className="bg-white rounded-lg border border-gray-200">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-xl font-semibold text-gray-900">Task List</h2>
        </div>
        
        <div className="divide-y divide-gray-200">
          {roadmapItems.map((item) => (
            <div key={item.id} className="px-6 py-4">
              <div className="flex items-center justify-between">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center">
                    <h3 className="text-lg font-medium text-gray-900">{item.task}</h3>
                    <span className={`ml-3 inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStatusColor(item.status)}`}>
                      {item.status.replace('_', ' ')}
                    </span>
                  </div>
                  <p className="text-sm text-gray-500 mt-1">Quarter: {item.quarter}</p>
                </div>
                
                <div className="flex items-center space-x-4">
                  <div className="w-32">
                    <div className="flex justify-between text-sm text-gray-600 mb-1">
                      <span>Progress</span>
                      <span>{item.progress_percent}%</span>
                    </div>
                    <div className="w-full bg-gray-200 rounded-full h-2">
                      <div 
                        className="bg-blue-600 h-2 rounded-full" 
                        style={{ width: `${item.progress_percent}%` }}
                      ></div>
                    </div>
                  </div>
                  
                  <div className="flex space-x-2">
                    <button
                      onClick={() => handleUpdateProgress(item.id, item.progress_percent - 10)}
                      className="px-3 py-1 text-sm text-gray-700 bg-gray-100 rounded-md hover:bg-gray-200"
                    >
                      -
                    </button>
                    <button
                      onClick={() => handleUpdateProgress(item.id, item.progress_percent + 10)}
                      className="px-3 py-1 text-sm text-gray-700 bg-gray-100 rounded-md hover:bg-gray-200"
                    >
                      +
                    </button>
                  </div>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}