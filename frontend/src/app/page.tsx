'use client'

import { useState, useEffect } from 'react'
import { Search, Plus, Edit, Trash2 } from 'lucide-react'
import * as api from '../lib/api'

export default function KnowledgeBase() {
  const [chapters] = useState([
    { id: 1, name: 'PRODUKT', description: 'Vision, Features, User Stories, Pricing, Marketing' },
    { id: 2, name: 'ARCHITEKTUR', description: 'Tech Stack, DSGVO, API, DB Schema, Deployment, Security' },
    { id: 3, name: 'ENTWICKLUNG', description: 'Roadmap, Sprint Planning, Code Guidelines, Testing' },
    { id: 4, name: 'DOKUMENTATION', description: 'API Docs, Setup Guides, User Manual, FAQ' },
    { id: 5, name: 'TESTING & QUALITÄT', description: 'Unit/Integration/E2E Tests, KI-Autonomous QA, DSGVO Tests' }
  ])
  
  const [entries, setEntries] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [selectedChapter, setSelectedChapter] = useState(null)
  const [searchQuery, setSearchQuery] = useState('')
  const [isEditing, setIsEditing] = useState(false)
  const [currentEntry, setCurrentEntry] = useState(null)
  
  useEffect(() => {
    loadEntries()
  }, [])
  
  const loadEntries = async () => {
    try {
      setLoading(true)
      const documents = await api.getDocuments()
      setEntries(documents)
    } catch (err) {
      setError('Failed to load documents')
      console.error(err)
    } finally {
      setLoading(false)
    }
  }
  
  const filteredEntries = entries.filter(entry => 
    (selectedChapter ? entry.chapter === selectedChapter : true) &&
    (searchQuery ? entry.title.toLowerCase().includes(searchQuery.toLowerCase()) || 
                   entry.content.toLowerCase().includes(searchQuery.toLowerCase()) : true)
  )
  
  const handleCreateEntry = () => {
    setCurrentEntry({ id: null, title: '', content: '', chapter: selectedChapter || 'PRODUKT', document_type: 'knowledge_base' })
    setIsEditing(true)
  }
  
  const handleEditEntry = (entry) => {
    setCurrentEntry(entry)
    setIsEditing(true)
  }
  
  const handleDeleteEntry = async (id) => {
    try {
      await api.deleteDocument(id)
      setEntries(entries.filter(entry => entry.id !== id))
    } catch (err) {
      setError('Failed to delete entry')
      console.error(err)
    }
  }
  
  const handleSaveEntry = async () => {
    try {
      if (currentEntry.id) {
        // Update existing entry
        const updatedEntry = await api.updateDocument(currentEntry.id, {
          title: currentEntry.title,
          content: currentEntry.content,
          document_type: currentEntry.chapter
        })
        setEntries(entries.map(entry => 
          entry.id === currentEntry.id ? updatedEntry : entry
        ))
      } else {
        // Create new entry
        const newEntry = await api.createDocument({
          title: currentEntry.title,
          content: currentEntry.content,
          document_type: currentEntry.chapter
        })
        setEntries([...entries, newEntry])
      }
      setIsEditing(false)
      setCurrentEntry(null)
    } catch (err) {
      setError('Failed to save entry')
      console.error(err)
    }
  }
  
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
    <div className="flex h-screen bg-gray-50">
      {/* Sidebar */}
      <div className="w-64 bg-white border-r border-gray-200 flex flex-col">
        <div className="p-4 border-b border-gray-200">
          <h1 className="text-xl font-bold text-gray-800">LexWolf Docs</h1>
          <p className="text-sm text-gray-500">Knowledge Base</p>
        </div>
        
        <div className="p-4">
          <div className="relative">
            <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400 w-4 h-4" />
            <input
              type="text"
              placeholder="Search documentation..."
              className="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>
        </div>
        
        <div className="flex-1 overflow-y-auto">
          <div className="px-4 py-2">
            <h2 className="text-xs font-semibold text-gray-500 uppercase tracking-wider">Chapters</h2>
            <ul className="mt-2 space-y-1">
              {chapters.map((chapter) => (
                <li key={chapter.id}>
                  <button
                    onClick={() => setSelectedChapter(selectedChapter === chapter.name ? null : chapter.name)}
                    className={`w-full text-left px-3 py-2 rounded-md text-sm ${
                      selectedChapter === chapter.name 
                        ? 'bg-blue-100 text-blue-700 font-medium' 
                        : 'text-gray-700 hover:bg-gray-100'
                    }`}
                  >
                    {chapter.name}
                  </button>
                </li>
              ))}
            </ul>
          </div>
        </div>
        
        <div className="p-4 border-t border-gray-200">
          <button
            onClick={handleCreateEntry}
            className="w-full flex items-center justify-center px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
          >
            <Plus className="w-4 h-4 mr-2" />
            New Entry
          </button>
        </div>
      </div>
      
      {/* Main Content */}
      <div className="flex-1 flex flex-col overflow-hidden">
        {/* Header */}
        <div className="bg-white border-b border-gray-200 px-6 py-4">
          <div className="flex items-center justify-between">
            <div>
              <h1 className="text-2xl font-bold text-gray-900">
                {selectedChapter ? selectedChapter : 'All Documentation'}
              </h1>
              <p className="text-gray-500">
                {filteredEntries.length} entries
              </p>
            </div>
          </div>
        </div>
        
        {/* Content */}
        <div className="flex-1 overflow-y-auto p-6">
          {isEditing ? (
            <EditEntryForm 
              entry={currentEntry} 
              setEntry={setCurrentEntry} 
              onSave={handleSaveEntry} 
              onCancel={() => setIsEditing(false)} 
            />
          ) : (
            <div className="grid grid-cols-1 gap-4">
              {filteredEntries.map((entry) => (
                <div key={entry.id} className="bg-white rounded-lg border border-gray-200 p-5 hover:shadow-sm transition-shadow">
                  <div className="flex items-start justify-between">
                    <div>
                      <h3 className="text-lg font-semibold text-gray-900">{entry.title}</h3>
                      <p className="mt-2 text-gray-600 line-clamp-3">{entry.content}</p>
                    </div>
                    <div className="flex space-x-2">
                      <button 
                        onClick={() => handleEditEntry(entry)}
                        className="p-2 text-gray-500 hover:text-blue-600 hover:bg-blue-50 rounded-md"
                      >
                        <Edit className="w-4 h-4" />
                      </button>
                      <button 
                        onClick={() => handleDeleteEntry(entry.id)}
                        className="p-2 text-gray-500 hover:text-red-600 hover:bg-red-50 rounded-md"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  </div>
                  <div className="mt-4 flex items-center text-sm text-gray-500">
                    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
                      {entry.document_type || 'General'}
                    </span>
                    <span className="ml-3">Last updated {new Date(entry.updated_at || entry.created_at).toLocaleDateString()}</span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function EditEntryForm({ entry, setEntry, onSave, onCancel }) {
  return (
    <div className="bg-white rounded-lg border border-gray-200 p-6">
      <h2 className="text-xl font-bold text-gray-900 mb-6">
        {entry.id ? 'Edit Entry' : 'Create New Entry'}
      </h2>
      
      <div className="space-y-6">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            Title
          </label>
          <input
            type="text"
            className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
            value={entry.title}
            onChange={(e) => setEntry({...entry, title: e.target.value})}
          />
        </div>
        
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            Chapter
          </label>
          <select
            className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
            value={entry.document_type || entry.chapter}
            onChange={(e) => setEntry({...entry, chapter: e.target.value, document_type: e.target.value})}
          >
            <option value="PRODUKT">PRODUKT</option>
            <option value="ARCHITEKTUR">ARCHITEKTUR</option>
            <option value="ENTWICKLUNG">ENTWICKLUNG</option>
            <option value="DOKUMENTATION">DOKUMENTATION</option>
            <option value="TESTING & QUALITÄT">TESTING & QUALITÄT</option>
          </select>
        </div>
        
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            Content
          </label>
          <textarea
            rows={10}
            className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
            value={entry.content}
            onChange={(e) => setEntry({...entry, content: e.target.value})}
          />
        </div>
        
        <div className="flex justify-end space-x-3 pt-4">
          <button
            onClick={onCancel}
            className="px-4 py-2 text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            onClick={onSave}
            className="px-4 py-2 text-white bg-blue-600 rounded-lg hover:bg-blue-700"
          >
            Save Entry
          </button>
        </div>
      </div>
    </div>
  )
}